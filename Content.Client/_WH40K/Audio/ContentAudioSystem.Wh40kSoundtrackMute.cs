using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Client.Audio;

public sealed partial class ContentAudioSystem
{
    private static readonly float Wh40kMuteFloor = SharedAudioSystem.VolumeToGain(MinVolume);

    private readonly HashSet<EntityUid> _wh40kMutedPausedStreams = new();

    private bool IsWh40kMusicPaused(EntityUid? uid)
    {
        return uid is { } stream && _wh40kMutedPausedStreams.Contains(stream);
    }

    /// <summary>
    /// Scales playback gain independently of track fades and pauses the soundtrack at zero.
    /// </summary>
    public void MuteSoundtrackWh40k(float level)
    {
        var lobbyStream = _lobbySoundtrackInfo?.MusicStreamEntityUid;

        if (level <= 0f)
        {
            PauseWh40kMusicStream(_ambientMusicStream);
            PauseWh40kMusicStream(lobbyStream);
        }
        else
        {
            foreach (var uid in _wh40kMutedPausedStreams)
            {
                if (!TryComp<AudioComponent>(uid, out var pausedAudio) || pausedAudio.State != AudioState.Paused)
                    continue;

                if (_lobbySoundtrackInfo is { } lobby && lobby.MusicStreamEntityUid == uid && pausedAudio.PauseTime is { } pausedAt)
                    _lobbySoundtrackInfo = lobby with { NextTrackOn = lobby.NextTrackOn + (_timing.CurTime - pausedAt) };

                _audio.SetState(uid, AudioState.Playing, component: pausedAudio);
            }

            _wh40kMutedPausedStreams.Clear();
        }

        foreach (var fading in _fadingOut.Keys)
        {
            ScaleWh40kMusicGain(fading, level);
        }

        ScaleWh40kMusicGain(_ambientMusicStream, level);
        ScaleWh40kMusicGain(lobbyStream, level);
    }

    private void PauseWh40kMusicStream(EntityUid? uid)
    {
        if (uid == null || !TryComp<AudioComponent>(uid, out var audio))
            return;

        var restarted = audio.State == AudioState.Paused && audio.Playing;
        if (audio.State != AudioState.Playing && !restarted)
            return;

        _audio.SetState(uid, AudioState.Paused, force: restarted, component: audio);
        _wh40kMutedPausedStreams.Add(uid.Value);
    }

    private void ScaleWh40kMusicGain(EntityUid? uid, float level)
    {
        if (!TryComp<AudioComponent>(uid, out var audio) || !audio.Playing)
            return;

        var gain = SharedAudioSystem.VolumeToGain(audio.Params.Volume);
        if (_musicDuckDepth > 0)
            gain = MathF.Min(gain, _musicDuckGain);

        audio.Gain = gain * MathF.Max(level, Wh40kMuteFloor);
    }
}
