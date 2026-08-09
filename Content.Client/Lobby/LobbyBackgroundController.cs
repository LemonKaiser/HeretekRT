using System;
using System.IO;
using Content.Client.Lobby.UI;
using Content.Client.Message;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Lobby;

internal sealed class LobbyBackgroundController
{
    private const string LobbyBackgroundModeServer = "server";
    private const string LobbyBackgroundModeStatic = "static";
    private const string LobbyBackgroundModeAnimated = "animated";

    private readonly IConfigurationManager _cfg;
    private readonly IPrototypeManager _protoMan;
    private readonly IResourceCache _resourceCache;
    private readonly IClyde _clyde;
    private readonly Func<string> _getServerBackgroundId;
    private readonly ISawmill _sawmill = Logger.GetSawmill("lobby");

    private LobbyGui? _lobby;
    private LobbyGifStreamPlayer? _gifPlayer;
    private ResPath? _activeStaticBackground;
    private ResPath? _activeAnimatedBackground;

    private enum LobbyBackgroundLoadPreference : byte
    {
        ServerDefault,
        PreferStatic,
        PreferAnimated,
    }

    private enum GifLoadResult : byte
    {
        Loaded,
        Pending,
        Failed,
    }

    public LobbyBackgroundController(
        IConfigurationManager cfg,
        IPrototypeManager protoMan,
        IResourceCache resourceCache,
        IClyde clyde,
        IGameTiming gameTiming,
        Func<string> getServerBackgroundId)
    {
        _cfg = cfg;
        _protoMan = protoMan;
        _resourceCache = resourceCache;
        _clyde = clyde;
        _getServerBackgroundId = getServerBackgroundId;
        _ = gameTiming;
    }

    public void Startup(LobbyGui lobby)
    {
        _lobby = lobby;
        DetachCurrentBackgroundTexture();
        _cfg.OnValueChanged(CCVars.LobbyBackgroundType, OnLobbyBackgroundConfigChanged, true);
        _cfg.OnValueChanged(CCVars.LobbyStaticBackground, OnLobbyBackgroundConfigChanged, true);
        _cfg.OnValueChanged(CCVars.LobbyAnimatedBackground, OnLobbyBackgroundConfigChanged, true);
        _cfg.OnValueChanged(CCVars.ReducedMotion, OnReducedMotionChanged, true);
        _cfg.OnValueChanged(CCVars.LobbyPanelOpacity, OnLobbyPanelOpacityChanged, true);
    }

    public void Shutdown()
    {
        _cfg.UnsubValueChanged(CCVars.LobbyBackgroundType, OnLobbyBackgroundConfigChanged);
        _cfg.UnsubValueChanged(CCVars.LobbyStaticBackground, OnLobbyBackgroundConfigChanged);
        _cfg.UnsubValueChanged(CCVars.LobbyAnimatedBackground, OnLobbyBackgroundConfigChanged);
        _cfg.UnsubValueChanged(CCVars.ReducedMotion, OnReducedMotionChanged);
        _cfg.UnsubValueChanged(CCVars.LobbyPanelOpacity, OnLobbyPanelOpacityChanged);
        ClearBackgroundState();
        _lobby = null;
    }

    public void FrameUpdate(float deltaSeconds)
    {
        UpdateGifStream(deltaSeconds);
    }

    public void RefreshBackground()
    {
        UpdateLobbyBackground();
    }

    private void OnLobbyBackgroundConfigChanged(string _)
    {
        UpdateLobbyBackground();
    }

    private void OnLobbyPanelOpacityChanged(float opacity)
    {
        _lobby?.ApplyPanelBackgroundOpacity(opacity);
    }

    private void OnReducedMotionChanged(bool _)
    {
        UpdateLobbyBackground();
    }

    private void UpdateLobbyBackground()
    {
        if (_lobby == null)
            return;

        var preference = GetLoadPreference();
        LobbyBackgroundPrototype? loadedProto = null;

        if (TryResolvePreferredBackground(out var preferredProto)
            && TryLoadBackgroundFromPrototype(preferredProto, preference))
        {
            loadedProto = preferredProto;
        }
        else if (TryResolveServerBackground(out var serverProto)
                 && (loadedProto == null || loadedProto.ID != serverProto.ID)
                 && TryLoadBackgroundFromPrototype(serverProto, LobbyBackgroundLoadPreference.ServerDefault))
        {
            loadedProto = serverProto;
        }

        if (loadedProto == null)
        {
            ClearBackgroundState();
            _lobby.LobbyBackground.SetMarkup(Loc.GetString("lobby-state-background-no-background-text"));
            return;
        }

        _lobby.LobbyBackground.SetMarkup(Loc.GetString(
            "lobby-state-background-text",
            ("backgroundTitle", Loc.GetString(loadedProto.Title)),
            ("backgroundArtist", Loc.GetString(loadedProto.Artist))));
    }

    private bool TryResolvePreferredBackground(out LobbyBackgroundPrototype proto)
    {
        if (_cfg.GetCVar(CCVars.ReducedMotion))
            return TryResolveConfiguredBackground(CCVars.LobbyStaticBackground, preferAnimated: false, out proto);

        var mode = _cfg.GetCVar(CCVars.LobbyBackgroundType).ToLowerInvariant();

        switch (mode)
        {
            case LobbyBackgroundModeStatic:
                if (TryResolveConfiguredBackground(CCVars.LobbyStaticBackground, preferAnimated: false, out proto))
                    return true;
                break;
            case LobbyBackgroundModeAnimated:
                if (TryResolveConfiguredBackground(CCVars.LobbyAnimatedBackground, preferAnimated: true, out proto))
                    return true;
                break;
            default:
                if (TryResolveServerBackground(out proto))
                    return true;
                if (TryResolveConfiguredBackground(CCVars.LobbyAnimatedBackground, preferAnimated: true, out proto))
                    return true;
                if (TryResolveConfiguredBackground(CCVars.LobbyStaticBackground, preferAnimated: false, out proto))
                    return true;
                break;
        }

        proto = default!;
        return false;
    }

    private bool TryResolveConfiguredBackground(
        CVarDef<string> cvar,
        bool preferAnimated,
        out LobbyBackgroundPrototype proto)
    {
        var configuredId = _cfg.GetCVar(cvar);
        if (!string.IsNullOrWhiteSpace(configuredId)
            && _protoMan.TryIndex<LobbyBackgroundPrototype>(configuredId, out var configuredProto)
            && HasRequestedBackgroundType(configuredProto, preferAnimated))
        {
            proto = configuredProto;
            return true;
        }

        if (TryResolveServerBackground(out var serverProto) && HasRequestedBackgroundType(serverProto, preferAnimated))
        {
            proto = serverProto;
            return true;
        }

        foreach (var candidate in _protoMan.EnumeratePrototypes<LobbyBackgroundPrototype>())
        {
            if (!HasRequestedBackgroundType(candidate, preferAnimated))
                continue;

            proto = candidate;
            return true;
        }

        proto = default!;
        return false;
    }

    private bool TryResolveServerBackground(out LobbyBackgroundPrototype proto)
    {
        var serverBackground = _getServerBackgroundId.Invoke();
        if (_protoMan.TryIndex(serverBackground, out LobbyBackgroundPrototype? serverProto))
        {
            proto = serverProto;
            return true;
        }

        proto = default!;
        return false;
    }

    private static bool HasRequestedBackgroundType(LobbyBackgroundPrototype proto, bool preferAnimated)
    {
        return preferAnimated ? proto.BackgroundGif != null : proto.Background != null;
    }

    private LobbyBackgroundLoadPreference GetLoadPreference()
    {
        if (_cfg.GetCVar(CCVars.ReducedMotion))
            return LobbyBackgroundLoadPreference.PreferStatic;

        var mode = _cfg.GetCVar(CCVars.LobbyBackgroundType).ToLowerInvariant();
        return mode switch
        {
            LobbyBackgroundModeStatic => LobbyBackgroundLoadPreference.PreferStatic,
            LobbyBackgroundModeAnimated => LobbyBackgroundLoadPreference.PreferAnimated,
            _ => LobbyBackgroundLoadPreference.ServerDefault,
        };
    }

    private bool TryLoadBackgroundFromPrototype(
        LobbyBackgroundPrototype proto,
        LobbyBackgroundLoadPreference preference)
    {
        switch (preference)
        {
            case LobbyBackgroundLoadPreference.PreferStatic:
                if (proto.Background is { } staticPreferred && TryLoadStaticBackground(staticPreferred))
                    return true;

                if (proto.BackgroundGif is { } gifFallbackStatic)
                {
                    var gifResult = TryLoadGifBackground(gifFallbackStatic);
                    return gifResult is GifLoadResult.Loaded or GifLoadResult.Pending;
                }

                return false;
            case LobbyBackgroundLoadPreference.PreferAnimated:
                if (proto.BackgroundGif is { } gifPreferred)
                {
                    var gifResult = TryLoadGifBackground(gifPreferred);
                    if (gifResult == GifLoadResult.Loaded)
                        return true;

                    if (gifResult == GifLoadResult.Pending)
                    {
                        if (!IsBackgroundVisible() && proto.Background is { } staticFallbackAnimated)
                            TryLoadStaticBackground(staticFallbackAnimated, isAnimatedFallback: true);

                        return true;
                    }
                }

                return proto.Background is { } staticFallbackPreferredAnimated
                       && TryLoadStaticBackground(staticFallbackPreferredAnimated);
            default:
                if (proto.BackgroundGif is { } gifPath)
                {
                    var gifResult = TryLoadGifBackground(gifPath);
                    if (gifResult == GifLoadResult.Loaded)
                        return true;

                    if (gifResult == GifLoadResult.Pending)
                    {
                        if (!IsBackgroundVisible() && proto.Background is { } staticFallbackDefault)
                            TryLoadStaticBackground(staticFallbackDefault, isAnimatedFallback: true);

                        return true;
                    }
                }

                return proto.Background is { } staticBackground && TryLoadStaticBackground(staticBackground);
        }
    }

    private bool TryLoadStaticBackground(ResPath path, bool isAnimatedFallback = false)
    {
        if (_activeStaticBackground == path
            && _lobby?.Background.Texture != null
            && (isAnimatedFallback || _gifPlayer == null))
        {
            return true;
        }

        if (!isAnimatedFallback)
            StopGifStream(clearTexture: false);

        try
        {
            var texture = _resourceCache.GetResource<TextureResource>(path).Texture;
            _lobby?.SetLobbyBackgroundIsAnimated(isAnimatedFallback);
            _lobby?.SetLobbyBackgroundTexture(texture);
            _activeStaticBackground = path;
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to load static lobby background '{Path}': {Error}", path, e);
            return false;
        }
    }

    private GifLoadResult TryLoadGifBackground(ResPath gifPath)
    {
        if (_activeAnimatedBackground == gifPath && _gifPlayer != null)
            return GifLoadResult.Loaded;

        StopGifStream(clearTexture: true);
        _lobby?.SetLobbyBackgroundIsAnimated(true);

        if (!TryReadGifData(gifPath, out var gifData))
        {
            _lobby?.SetLobbyBackgroundIsAnimated(false);
            return GifLoadResult.Failed;
        }

        try
        {
            var player = new LobbyGifStreamPlayer(_clyde);
            player.Start(gifData);
            _gifPlayer = player;
            _activeAnimatedBackground = gifPath;
            return GifLoadResult.Pending;
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to start animated lobby background '{Path}': {Error}", gifPath, e);
            _lobby?.SetLobbyBackgroundIsAnimated(false);
            return GifLoadResult.Failed;
        }
    }

    private bool TryReadGifData(ResPath path, out byte[] gifData)
    {
        gifData = Array.Empty<byte>();

        try
        {
            using var stream = _resourceCache.ContentFileRead(path);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            gifData = memory.ToArray();
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to read animated lobby background GIF '{Path}': {Error}", path, e);
            return false;
        }
    }

    private bool IsBackgroundVisible()
    {
        return _lobby?.Background.Texture != null;
    }

    private void UpdateGifStream(float deltaSeconds)
    {
        var player = _gifPlayer;
        if (player == null)
            return;

        if (player.FrameUpdate(deltaSeconds, out var texture, out var failure))
        {
            if (texture != null)
                _lobby?.SetLobbyBackgroundTexture(texture);

            return;
        }

        _sawmill.Error("Animated lobby background stream failed: {Error}", failure);
        var fallback = _activeStaticBackground;
        StopGifStream(clearTexture: true);

        if (fallback != null && TryLoadStaticBackground(fallback.Value))
            return;

        _lobby?.SetLobbyBackgroundIsAnimated(false);
    }

    private void ClearBackgroundState()
    {
        StopGifStream(clearTexture: true);
        _activeStaticBackground = null;
        _lobby?.SetLobbyBackgroundIsAnimated(false);
    }

    private void StopGifStream(bool clearTexture)
    {
        if (_gifPlayer != null)
        {
            var metrics = _gifPlayer.GetMetrics();
            _sawmill.Debug(
                "Animated lobby background stopped after {UploadedFrames} uploads, {DroppedFrames} dropped frames and {AverageUploadMilliseconds:F3} ms average upload time.",
                metrics.UploadedFrames,
                metrics.DroppedFrames,
                metrics.AverageUploadMilliseconds);
            _gifPlayer.Dispose();
        }

        _gifPlayer = null;
        _activeAnimatedBackground = null;

        if (clearTexture)
            DetachCurrentBackgroundTexture();
    }

    private void DetachCurrentBackgroundTexture()
    {
        _lobby?.SetLobbyBackgroundTexture(null);
    }
}
