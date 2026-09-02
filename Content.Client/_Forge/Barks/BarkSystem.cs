using Content.Client.Audio;
using Content.Shared._Forge.Barks;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Forge.Barks;

/// <summary>
///     Plays bark sounds received from the server.
/// </summary>
public sealed class BarkSystem : EntitySystem
{
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private const float MinimalVolume = -10f;
    private const float WhisperFade = 4f;
    private const float VoiceRange = 10f;
    private const float WhisperMuffledRange = 5f;
    private const int MaxBarksPerMessage = 64;

    public override void Initialize()
    {
        SubscribeNetworkEvent<PlayBarkEvent>(OnPlayBark);
        SubscribeNetworkEvent<PlayBarkPreviewEvent>(OnPlayBarkPreview);
    }

    public void RequestPreviewBark(string barkVoiceId)
    {
        RaiseNetworkEvent(new RequestPreviewBarkEvent(barkVoiceId));
    }

    private void OnPlayBark(PlayBarkEvent ev)
    {
        if (!TryGetEntity(ev.SourceUid, out var source) || Deleted(source.Value))
            return;

        var speed = Math.Clamp(ev.PlaybackSpeed, 0.25f, 4f);
        var parameters = AudioParams.Default
            .WithVolume(GetVolume(ev.IsWhisper))
            .WithMaxDistance(ev.IsWhisper ? WhisperMuffledRange : VoiceRange);

        var soundCount = Math.Clamp(
            (int) Math.Ceiling(ev.Message.Length * 0.05f / (0.15f / speed)),
            1,
            MaxBarksPerMessage);
        var interval = TimeSpan.FromSeconds(0.15f / speed);

        for (var i = 0; i < soundCount; i++)
        {
            var delay = interval * i;
            Timer.Spawn(delay, () => PlayAtEntity(ev.SoundPath, source.Value, parameters));
        }
    }

    private void OnPlayBarkPreview(PlayBarkPreviewEvent ev)
    {
        var audio = LoadAudio(ev.SoundPath);
        if (audio == null)
            return;

        var specifier = new ResolvedPathSpecifier(ev.SoundPath);
        _audio.PlayGlobal(audio.AudioStream, specifier, AudioParams.Default.WithVolume(GetVolume(false)));
    }

    private void PlayAtEntity(string soundPath, EntityUid source, AudioParams parameters)
    {
        if (Deleted(source))
            return;

        var audio = LoadAudio(soundPath);
        if (audio == null)
            return;

        _audio.PlayEntity(audio.AudioStream, source, new ResolvedPathSpecifier(soundPath), parameters);
    }

    private AudioResource? LoadAudio(string soundPath)
    {
        try
        {
            var audio = new AudioResource();
            audio.Load(IoCManager.Instance!, new ResPath(soundPath));
            return audio;
        }
        catch (Exception exception)
        {
            Log.Warning($"Failed to load bark audio '{soundPath}': {exception.Message}");
            return null;
        }
    }

    private float GetVolume(bool isWhisper)
    {
        var volume = MinimalVolume + SharedAudioSystem.GainToVolume(_cfg.GetCVar(CCVars.BarksVolume));
        return isWhisper ? volume - WhisperFade : volume;
    }
}
