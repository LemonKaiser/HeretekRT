using System.Numerics;
using Content.Shared._WH40K.Visuals.SpecialScreenAnimation;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Visuals.SpecialScreenAnimation.Overlays;

public sealed partial class SpecialScreenAnimationOverlay : Overlay
{
    private const int MaximumQueuedAnimations = 8;

    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private readonly Queue<SpecialScreenAnimationData> _queue = new();
    private ActiveAnimation? _active;
    private (Font Font, string Path, int Size)? _font;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public SpecialScreenAnimationOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 102;
    }

    public void Enqueue(SpecialScreenAnimationData animation)
    {
        if (animation.Sprite == null || animation.TotalDuration <= 0f)
            return;

        if (_queue.Count >= MaximumQueuedAnimations)
            _queue.Dequeue();

        _queue.Enqueue(animation.Copy());
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_players.LocalEntity == null)
            return;

        if (_active == null)
        {
            if (!_queue.TryDequeue(out var animation))
                return;

            _active = new ActiveAnimation(animation, _timing.RealTime);
        }

        var active = _active.Value;
        var elapsed = (float) (_timing.RealTime - active.StartedAt).TotalSeconds;
        if (elapsed >= active.Data.TotalDuration)
        {
            _active = null;
            return;
        }

        var progress = Math.Clamp(elapsed / active.Data.TotalDuration, 0f, 1f);
        var opacity = GetOpacity(active.Data, elapsed);
        var position = Vector2.Lerp(active.Data.StartPosition, active.Data.EndPosition, progress);
        var uiScale = _config.GetCVar(CVars.DisplayUIScale);
        if (uiScale == 0f)
            uiScale = _ui.DefaultUIScale;

        if (active.Data.Sprite is not { } sprite)
            return;

        var center = _clyde.ScreenSize / 2;
        var texture = ResolveTexture(sprite);
        if (texture == null)
            return;

        var box = UIBox2.FromDimensions(center + position, texture.Size * uiScale * active.Data.Scale);
        args.ScreenHandle.DrawTextureRect(texture, box, Color.White.WithAlpha(opacity));

        if (string.IsNullOrWhiteSpace(active.Data.Text))
            return;

        var font = GetFont(active.Data);
        args.ScreenHandle.DrawString(font, center + active.Data.TextPosition, active.Data.Text,
            active.Data.TextColor.WithAlpha(opacity));
    }

    private Font GetFont(SpecialScreenAnimationData animation)
    {
        if (_font is { } cached && cached.Path == animation.TextFontPath && cached.Size == animation.TextFontSize)
            return cached.Font;

        var font = new VectorFont(_resources.GetResource<FontResource>(animation.TextFontPath), animation.TextFontSize);
        _font = (font, animation.TextFontPath, animation.TextFontSize);
        return font;
    }

    private Texture? ResolveTexture(SpriteSpecifier sprite)
    {
        try
        {
            switch (sprite)
            {
                case SpriteSpecifier.Texture texture:
                {
                    var path = texture.TexturePath.IsRooted
                        ? texture.TexturePath
                        : SpriteSpecifierSerializer.TextureRoot / texture.TexturePath;
                    return _resources.GetResource<TextureResource>(path).Texture;
                }
                case SpriteSpecifier.Rsi rsi:
                {
                    var path = rsi.RsiPath.IsRooted
                        ? rsi.RsiPath
                        : SpriteSpecifierSerializer.TextureRoot / rsi.RsiPath;
                    var state = _resources.GetResource<RSIResource>(path).RSI;
                    return state.TryGetState(rsi.RsiState, out var frame)
                        ? frame.GetFrame(RsiDirection.South, 0)
                        : null;
                }
            }
        }
        catch (Exception)
        {
            // Presentation data may reference an optional resource. A bad animation must not break the HUD.
        }

        return null;
    }

    private static float GetOpacity(SpecialScreenAnimationData animation, float elapsed)
    {
        if (animation.FadeInDuration > 0f && elapsed < animation.FadeInDuration)
            return animation.MaxOpacity * elapsed / animation.FadeInDuration;

        var fadeOutStart = animation.TotalDuration - animation.FadeOutDuration;
        if (animation.FadeOutDuration > 0f && elapsed > fadeOutStart)
            return animation.MaxOpacity * (animation.TotalDuration - elapsed) / animation.FadeOutDuration;

        return animation.MaxOpacity;
    }

    private readonly record struct ActiveAnimation(SpecialScreenAnimationData Data, TimeSpan StartedAt);
}
