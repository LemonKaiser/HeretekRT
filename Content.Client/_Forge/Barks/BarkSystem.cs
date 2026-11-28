using Content.Client.Audio;
using Content.Shared._Forge.Barks;
using Content.Shared._Forge.Text;
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
public sealed partial class BarkSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float MinimalVolume = -10f;
    private const float WhisperFade = 4f;
    private const float VoiceRange = 10f;
    private const float WhisperMuffledRange = 5f;
    private const int MaxPendingBarks = 512;

    private readonly List<PendingBark> _pendingBarks = [];
    private readonly HashSet<string> _missingAudio = [];

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
        if (!TryGetEntity(ev.SourceUid, out var source) ||
            Deleted(source.Value) ||
            ev.SoundPaths.Length == 0 ||
            ev.Barks.Length == 0)
            return;

        var parameters = AudioParams.Default
            .WithVolume(GetVolume(ev.IsWhisper))
            .WithMaxDistance(ev.IsWhisper ? WhisperMuffledRange : VoiceRange);

        // A new phrase from the same speaker supersedes any unheard tail of the old one.
        _pendingBarks.RemoveAll(bark => bark.Source == source.Value);

        var available = Math.Max(0, MaxPendingBarks - _pendingBarks.Count);
        var startTime = _timing.CurTime;
        var localTimingScale = GetLocalTimingScale(ev.PlaybackSpeed);

        foreach (var bark in ev.Barks)
        {
            if ((uint) bark.SoundIndex >= (uint) ev.SoundPaths.Length)
                continue;

            var delay = Math.Max(0f, bark.Delay) / localTimingScale;
            if (available-- <= 0)
                break;

            var soundPath = ev.SoundPaths[bark.SoundIndex];
            if (string.IsNullOrWhiteSpace(soundPath))
                continue;

            var barkParameters = parameters
                .WithPitchScale(Math.Clamp(bark.PitchScale, 0.25f, 4f))
                .AddVolume(Math.Clamp(bark.VolumeOffset, -3f, 3f));

            _pendingBarks.Add(new PendingBark(
                startTime + TimeSpan.FromSeconds(delay),
                source.Value,
                soundPath,
                barkParameters));
        }

        _pendingBarks.Sort(static (left, right) => left.PlayAt.CompareTo(right.PlayAt));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        while (_pendingBarks.Count > 0 && _pendingBarks[0].PlayAt <= _timing.CurTime)
        {
            var bark = _pendingBarks[0];
            _pendingBarks.RemoveAt(0);
            PlayAtEntity(bark.SoundPath, bark.Source, bark.Parameters);
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
        if (_resources.TryGetResource(soundPath, out AudioResource? audio))
            return audio;

        if (_missingAudio.Add(soundPath))
            Log.Warning($"Failed to load bark audio '{soundPath}'.");

        return null;
    }

    private float GetVolume(bool isWhisper)
    {
        var volume = MinimalVolume + SharedAudioSystem.GainToVolume(_cfg.GetCVar(CCVars.BarksVolume));
        return isWhisper ? volume - WhisperFade : volume;
    }

    private float GetLocalTimingScale(float speakerPlaybackSpeed)
    {
        if (!_cfg.GetCVar(CCVars.TypewriterTextEnabled) || _cfg.GetCVar(CCVars.ReducedMotion))
            return 1f;

        var speakerSpeed = TextRevealTiming.ClampSpeedMultiplier(speakerPlaybackSpeed);
        var visualSpeed = TextRevealTiming.ClampSpeedMultiplier(
            speakerSpeed * TextRevealTiming.ClampSpeedMultiplier(_cfg.GetCVar(CCVars.TypewriterTextSpeed)));
        return visualSpeed / speakerSpeed;
    }

    private readonly record struct PendingBark(
        TimeSpan PlayAt,
        EntityUid Source,
        string SoundPath,
        AudioParams Parameters);
}
