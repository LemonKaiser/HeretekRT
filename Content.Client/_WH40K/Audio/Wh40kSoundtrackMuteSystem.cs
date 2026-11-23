using Content.Client.Audio;
using Content.Client.Instruments;
using Content.Shared.Audio.Jukebox;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Shared;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;

namespace Content.Client._WH40K.Audio;

/// <summary>Fades background music out while a jukebox or MIDI instrument is in range.</summary>
public sealed partial class Wh40kSoundtrackMuteSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ContentAudioSystem _contentAudio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const float CheckInterval = 1f;

    private readonly Wh40kSoundtrackMuteLevel _level = new();

    private bool _enabled;
    private float _midiVolume;
    private float _untilCheck;
    private bool _audible;
    private float _lastLevel = 1f;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(AudioSystem));

        Subs.CVar(_cfg, CCVars.SoundtrackMuteEnabled, value => _enabled = value, true);
        Subs.CVar(_cfg, CVars.MidiVolume, value => _midiVolume = value, true);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_enabled)
        {
            _level.Reset();
            _audible = false;
            Apply();
            return;
        }

        _untilCheck -= frameTime;
        if (_untilCheck <= 0f)
        {
            _untilCheck += CheckInterval;
            _audible = IsJukeboxAudible() || IsInstrumentAudible();
        }

        _level.Update(_audible, frameTime);
        Apply();
    }

    public override void Shutdown()
    {
        _contentAudio.MuteSoundtrackWh40k(1f);
        base.Shutdown();
    }

    private void Apply()
    {
        var level = _level.Level;
        if (level < 1f || _lastLevel < 1f)
            _contentAudio.MuteSoundtrackWh40k(level);

        _lastLevel = level;
    }

    private bool IsJukeboxAudible()
    {
        var listener = _audio.GetListenerCoordinates();
        var query = EntityQueryEnumerator<JukeboxComponent>();

        while (query.MoveNext(out _, out var jukebox))
        {
            if (jukebox.AudioStream is not { } stream
                || !TryComp<AudioComponent>(stream, out var audio)
                || !audio.Playing
                || audio.Gain <= 0f)
            {
                continue;
            }

            // The stream reaches the client across all of PVS, further than it can be heard.
            var position = _transform.GetMapCoordinates(stream);
            if (position.MapId != listener.MapId)
                continue;

            if ((position.Position - listener.Position).Length() <= audio.Params.MaxDistance)
                return true;
        }

        return false;
    }

    private bool IsInstrumentAudible()
    {
        if (_midiVolume <= 0f)
            return false;

        var listener = _audio.GetListenerCoordinates();
        var query = EntityQueryEnumerator<InstrumentComponent>();

        while (query.MoveNext(out _, out var instrument))
        {
            if (instrument.Renderer is not { TrackingCoordinates: { } position } || position.MapId != listener.MapId)
                continue;

            if ((position.Position - listener.Position).Length() <= SharedAudioSystem.DefaultSoundRange)
                return true;
        }

        return false;
    }
}
