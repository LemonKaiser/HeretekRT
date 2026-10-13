using Content.Client.Audio;
using Content.Client.Gameplay;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Client.State;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._WH40K.Audio;

public sealed partial class Wh40kMusicOverrideSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ContentAudioSystem _contentAudio = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IStateManager _state = default!;

    private readonly Dictionary<object, MusicRequest> _requests = new();
    private readonly HashSet<object> _voiceDucks = new();
    private object? _owner;
    private MusicRequest? _current;
    private EntityUid? _stream;
    private float _volume;
    private bool _blocked;

    private sealed record MusicRequest(ResolvedSoundSpecifier Sound, AudioParams Audio, int Priority,
        float FadeIn, float FadeOut);

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
        SubscribeLocalEvent<PlayAmbientMusicEvent>(OnAmbient);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRestart);
        Subs.CVar(_configuration, CCVars.AmbientMusicVolume, OnVolume, true);
    }

    public void Request(object owner, ResolvedSoundSpecifier sound, AudioParams audio, int priority,
        float fadeIn = 0.5f, float fadeOut = 1f)
    {
        var request = new MusicRequest(sound, audio, priority, MathF.Max(fadeIn, 0), MathF.Max(fadeOut, 0));
        if (_requests.TryGetValue(owner, out var old) && old == request)
            return;
        _requests[owner] = request;
        Select();
    }

    public void Release(object owner, bool immediate = false, float? fadeOut = null)
    {
        if (!_requests.Remove(owner))
            return;
        if (fadeOut is { } duration && _current != null && Equals(_owner, owner))
            _current = _current with { FadeOut = MathF.Max(duration, 0) };
        if (immediate && Equals(_owner, owner))
            Stop(true);
        Select();
    }

    public void SetVoiceDuck(object owner, bool active)
    {
        var changed = active ? _voiceDucks.Add(owner) : _voiceDucks.Remove(owner);
        if (!changed)
            return;
        ApplyVolume();
    }

    private void OnVolume(float volume)
    {
        _volume = SharedAudioSystem.GainToVolume(volume);
        ApplyVolume();
    }

    private void OnAmbient(ref PlayAmbientMusicEvent args)
    {
        args.Cancelled |= _blocked;
    }

    private void Select()
    {
        object? winner = null;
        MusicRequest? selected = null;
        foreach (var (owner, request) in _requests)
        {
            if (selected != null && (request.Priority < selected.Priority ||
                request.Priority == selected.Priority && !Equals(owner, _owner)))
                continue;
            winner = owner;
            selected = request;
        }

        if (Equals(winner, _owner) && selected == _current && _stream != null && Exists(_stream))
            return;

        Stop(false);
        _owner = winner;
        _current = selected;
        if (selected == null)
        {
            var wasBlocked = _blocked;
            _blocked = false;
            if (wasBlocked)
                _contentAudio.ResumeAmbientMusic();
            return;
        }

        if (!_blocked)
        {
            _blocked = true;
            _contentAudio.DisableAmbientMusic();
        }

        var audio = selected.Audio.WithLoop(true).WithVolume(selected.Audio.Volume + _volume - (_voiceDucks.Count > 0 ? 8f : 0f));
        var result = _audio.PlayGlobal(selected.Sound, Filter.Local(), false, audio);
        if (result == null)
            return;
        _stream = result.Value.Entity;
        if (selected.FadeIn > 0)
            _contentAudio.FadeIn(_stream, result.Value.Component, selected.FadeIn);
    }

    private void Stop(bool immediate)
    {
        if (_stream is { } stream && TryComp<AudioComponent>(stream, out var audio))
        {
            if (immediate || _current == null || _current.FadeOut <= 0)
            {
                audio.StopPlaying();
                QueueDel(stream);
            }
            else
                _contentAudio.FadeOut(stream, audio, _current.FadeOut);
        }
        _stream = null;
    }

    private void ApplyVolume()
    {
        if (_current != null && _stream is { } stream && TryComp<AudioComponent>(stream, out var audio))
            _audio.SetVolume(stream, _current.Audio.Volume + _volume - (_voiceDucks.Count > 0 ? 8f : 0f), audio);
    }

    private void Clear()
    {
        Stop(true);
        _requests.Clear();
        _voiceDucks.Clear();
        _owner = null;
        _current = null;
        _blocked = false;
    }

    private void OnRestart(RoundRestartCleanupEvent args)
    {
        Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_state.CurrentState is not GameplayState)
        {
            if (_blocked)
                Clear();
            return;
        }
        if (_requests.Count > 0 && (_stream == null || !Exists(_stream)))
            Select();
    }

    public override void Shutdown()
    {
        Clear();
        base.Shutdown();
    }
}
