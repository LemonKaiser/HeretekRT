using System.Numerics;
using Content.Shared._WH40K.ShipPa;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.Replays.Playback;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.ShipPa;

/// <summary>
/// Client-side spatial Vox-PA renderer: selects the nearest unoccluded speaker per listener (at most 2 voices
/// during handoffs), applies speaker damage distortion/flutter, and shows a WH40K Heritage subtitle banner.
/// </summary>
public sealed partial class Wh40kShipPaMeshSystem : EntitySystem
{
    [Dependency] private IReplayPlaybackManager _replay = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private float _personalVolume = 1f;
    private EntityUid? _subtitleGrid;
    private readonly List<Voice> _voices = new();
    private readonly HashSet<string> _failedPaths = new();
    private readonly HashSet<int> _captioned = new();
    private readonly Queue<int> _captionOrder = new();
    private Candidate? _selected;
    private float _selectionRemaining;
    private Label? _subtitle;
    private PanelContainer? _subtitlePanel;
    private TimeSpan _subtitleUntil;
    private int _subtitlePriority;
    private string? _lastCaption;
    private TimeSpan _lastCaptionAt;
    private bool _shuttingDown;

    private sealed class Voice
    {
        public EntityUid Entity;
        public EntityUid Speaker;
        public Wh40kShipPaBroadcast Broadcast = default!;
        public float Fade;
        public Wh40kShipPaDrift Drift;
    }

    private sealed record Candidate(EntityUid Grid, EntityUid Speaker, Wh40kShipPaBroadcast Broadcast, float Score);

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(AudioSystem));
        Subs.CVar(_cfg, Wh40kShipPaCVars.Volume, value => _personalVolume = Math.Clamp(value, Wh40kShipPaCVars.MinVolume, 1f), true);
    }

    public override void Shutdown()
    {
        _shuttingDown = true;
        StopVoices();
        if (_subtitlePanel is { Disposed: false })
            _subtitlePanel.Orphan();

        _subtitlePanel = null;
        _subtitle = null;
        base.Shutdown();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (_shuttingDown)
            return;

        _selectionRemaining -= frameTime;
        if (_selectionRemaining <= 0f)
        {
            _selectionRemaining = 0.1f;
            Select();
            SelectCaption();
        }

        Reconcile();
        for (var i = _voices.Count - 1; i >= 0; i--)
        {
            var voice = _voices[i];
            if (!TryComp(voice.Entity, out AudioComponent? audio))
            {
                _voices.RemoveAt(i);
                continue;
            }

            var wanted = _selected is { } selected &&
                         selected.Speaker == voice.Speaker &&
                         selected.Broadcast.Id == voice.Broadcast.Id &&
                         IsAvailable(selected);
            voice.Fade = Math.Clamp(voice.Fade + (wanted ? 1f : -1f) * frameTime / Wh40kShipPaPlaybackPolicy.FadeSeconds, 0f, 1f);
            if (voice.Fade == 0f && !wanted)
            {
                StopVoice(voice);
                _voices.RemoveAt(i);
                continue;
            }

            Apply(voice, audio);
        }

        if (_subtitlePanel != null)
        {
            var remaining = (float) (_subtitleUntil - _timing.RealTime).TotalSeconds;
            _subtitlePanel.Visible = remaining > 0f;
            _subtitlePanel.Modulate = new Color(1f, 1f, 1f, Math.Clamp(remaining, 0f, 1f));
        }
    }

    private bool IsAvailable(Candidate candidate)
    {
        if (_failedPaths.Contains(candidate.Broadcast.Path) || !candidate.Broadcast.IsActive(_timing.CurTime)
            || !TryComp(candidate.Grid, out Wh40kShipPaBroadcastComponent? state)
            || !state.Broadcasts.Exists(b => b.Id == candidate.Broadcast.Id)
            || !TryComp(candidate.Speaker, out Wh40kShipPaSpeakerComponent? speaker) || !speaker.Enabled
            || !TryComp(candidate.Speaker, out TransformComponent? xform)
            || !xform.Anchored || xform.GridUid != candidate.Grid || TerminatingOrDeleted(candidate.Speaker))
            return false;

        var listener = _audio.GetListenerCoordinates();
        return xform.MapID == listener.MapId &&
               (_xform.GetWorldPosition(candidate.Speaker) - listener.Position).Length() < speaker.Range;
    }

    private void Select()
    {
        Candidate? best = null;
        Candidate? current = null;
        var listener = _audio.GetListenerCoordinates();
        var query = EntityQueryEnumerator<Wh40kShipPaSpeakerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var speaker, out var xform))
        {
            if (!speaker.Enabled || !xform.Anchored || xform.MapID != listener.MapId
                || xform.GridUid is not { } grid || !TryComp(grid, out Wh40kShipPaBroadcastComponent? state)
                || state.Broadcasts.Count == 0)
                continue;

            var delta = _xform.GetWorldPosition(uid) - listener.Position;
            var distance = delta.Length();
            if (distance >= speaker.Range)
                continue;

            var occlusion = _audio.GetOcclusion(listener, delta, distance, uid);
            var score = Wh40kShipPaPlaybackPolicy.Score(distance, speaker.Range, occlusion);
            foreach (var broadcast in state.Broadcasts)
            {
                if (_failedPaths.Contains(broadcast.Path))
                    continue;

                var candidate = new Candidate(grid, uid, broadcast, score);
                var hasAsset = !string.IsNullOrEmpty(broadcast.Path) &&
                               _resources.ContentFileExists(new ResPath(broadcast.Path));
                if (!broadcast.IsActive(_timing.CurTime) || !hasAsset)
                    continue;

                if (_selected is { } old && old.Speaker == uid && old.Broadcast.Id == broadcast.Id)
                    current = candidate;
                if (Better(candidate, best))
                    best = candidate;
            }
        }

        if (best != null && current != null && best.Broadcast.Id == current.Broadcast.Id &&
            !Wh40kShipPaPlaybackPolicy.ShouldSwitch(current.Score, best.Score))
            best = current;

        _selected = best;
    }

    private void SelectCaption()
    {
        EntityUid? grid = null;
        if (TryComp(_players.LocalEntity, out TransformComponent? playerTransform))
            grid = playerTransform.GridUid;
        else if (_maps.TryFindGridAt(_audio.GetListenerCoordinates(), out var eyeGrid, out _))
            grid = eyeGrid;

        if (_subtitleGrid != grid)
            _subtitleUntil = TimeSpan.Zero;
        _subtitleGrid = grid;
        if (grid == null || !TryComp(grid, out Wh40kShipPaBroadcastComponent? state))
            return;

        Wh40kShipPaBroadcast? caption = null;
        foreach (var broadcast in state.Broadcasts)
        {
            if (_timing.RealTime < _subtitleUntil && broadcast.Priority < _subtitlePriority)
                continue;
            if (broadcast.Caption == null || _captioned.Contains(broadcast.Id)
                || _failedPaths.Contains(broadcast.Path)
                || (!broadcast.Loop && _timing.CurTime >= broadcast.RetainUntil))
                continue;
            if (caption == null || broadcast.Priority > caption.Priority
                || (broadcast.Priority == caption.Priority && broadcast.Id > caption.Id))
                caption = broadcast;
        }

        if (caption != null)
            ShowCaption(grid.Value, caption);
    }

    private float ShipVolume(EntityUid? grid)
    {
        return TryComp(grid, out Wh40kShipAlertComponent? alert)
            ? Math.Clamp(alert.Volume, Wh40kShipAlertComponent.MinVolume, 1f)
            : 1f;
    }

    private static bool Better(Candidate candidate, Candidate? other)
    {
        if (other == null)
            return true;
        if (candidate.Broadcast.Priority != other.Broadcast.Priority)
            return candidate.Broadcast.Priority > other.Broadcast.Priority;
        if (candidate.Score != other.Score)
            return candidate.Score < other.Score;
        if (candidate.Broadcast.Id != other.Broadcast.Id)
            return candidate.Broadcast.Id > other.Broadcast.Id;
        return candidate.Speaker.Id < other.Speaker.Id;
    }

    private void Reconcile()
    {
        if (_selected is not { } target || !IsAvailable(target))
        {
            _selected = null;
            return;
        }

        foreach (var voice in _voices)
        {
            if (voice.Broadcast.Id == target.Broadcast.Id && voice.Speaker == target.Speaker && Exists(voice.Entity))
                return;
        }

        if (_voices.Count >= Wh40kShipPaPlaybackPolicy.MaxSources)
        {
            var drop = _voices[0].Fade <= _voices[1].Fade ? 0 : 1;
            StopVoice(_voices[drop]);
            _voices.RemoveAt(drop);
        }

        var speaker = Comp<Wh40kShipPaSpeakerComponent>(target.Speaker);
        var parameters = target.Broadcast.Params
            .WithLoop(target.Broadcast.Loop)
            .WithPitchScale(1f)
            .WithVariation(null)
            .WithMaxDistance(speaker.Range)
            .WithReferenceDistance(3f)
            .WithRolloffFactor(1.5f)
            .WithPlayOffset(target.Broadcast.Position(_timing.CurTime));
        var resolved = _audio.ResolveSound(new SoundPathSpecifier(target.Broadcast.Path));
        AudioResource resource;
        try
        {
            resource = _resources.GetResource<AudioResource>(target.Broadcast.Path, useFallback: false);
        }
        catch (Exception e)
        {
            _failedPaths.Add(target.Broadcast.Path);
            _selected = null;
            Log.Warning($"Cannot play Ship Vox-PA audio {target.Broadcast.Path}: {e.Message}");
            return;
        }

        var stream = _audio.PlayEntity(resource.AudioStream, target.Speaker, resolved, parameters.WithVolume(-80f));
        if (stream == null)
            return;

        _audio.SetPlaybackPosition(stream.Value.Entity, target.Broadcast.Position(_timing.CurTime));
        RemComp<TimedDespawnComponent>(stream.Value.Entity);
        var prepared = _timing.CurTime < target.Broadcast.Start;
        if (prepared)
        {
            var audio = Comp<AudioComponent>(stream.Value.Entity);
            audio.Pause();
            audio.PlaybackPosition = 0f;
        }

        var marker = EnsureComp<Wh40kShipPaAudioComponent>(stream.Value.Entity);
        marker.BroadcastId = target.Broadcast.Id;
        marker.Speaker = target.Speaker;
        _voices.Add(new Voice
        {
            Entity = stream.Value.Entity,
            Speaker = target.Speaker,
            Broadcast = target.Broadcast,
            Fade = prepared ? 1f : 0f,
        });
    }

    private void Apply(Voice voice, AudioComponent audio)
    {
        audio.Velocity = _players.LocalEntity is { } local ? _physics.GetMapLinearVelocity(local) : Vector2.Zero;

        if (!TryComp(voice.Speaker, out Wh40kShipPaSpeakerComponent? speaker)
            || !TryComp(voice.Speaker, out TransformComponent? xform))
        {
            audio.Gain = 0f;
            return;
        }

        var listener = _audio.GetListenerCoordinates();
        var delta = _xform.GetWorldPosition(voice.Speaker) - listener.Position;
        var distance = delta.Length();
        if (xform.MapID != listener.MapId || distance >= speaker.Range || !speaker.Enabled)
        {
            audio.Gain = 0f;
            return;
        }

        audio.Occlusion = _audio.GetOcclusion(listener, delta, distance, voice.Speaker) + 3f * speaker.Distortion;
        var tick = MathF.Floor((float) _timing.RealTime.TotalSeconds * 12f);
        var noise = MathF.Sin(tick * 12.9898f + voice.Speaker.Id * 78.233f) * 43758.5453f;
        noise -= MathF.Floor(noise);
        var flutter = noise > 1f - 0.8f * speaker.Distortion ? 0.15f : 1f;
        var edge = Math.Clamp((speaker.Range - distance) / 2f, 0f, 1f);
        audio.Gain = SharedAudioSystem.VolumeToGain(voice.Broadcast.Params.Volume + speaker.Volume - 6f * speaker.Distortion)
                     * voice.Fade * edge * flutter * ShipVolume(xform.GridUid) * _personalVolume;

        if (_timing.CurTime < voice.Broadcast.Start)
        {
            audio.Gain = 0f;
            audio.Pause();
            audio.PlaybackPosition = 0f;
            return;
        }

        if (_timing.Paused || (_replay.Replay != null && !_replay.Playing))
        {
            audio.Pause();
            voice.Drift = default;
            return;
        }

        if (!audio.Playing && voice.Broadcast.IsPlaying(_timing.CurTime))
            audio.StartPlaying();

        var expected = voice.Drift.Expected(
            voice.Broadcast.Position(_timing.CurTime),
            _timing.RealTime,
            voice.Broadcast.Length,
            voice.Broadcast.Loop);
        if (Wh40kShipPaDrift.NeedsSeek(audio.PlaybackPosition, expected, voice.Broadcast.Length, voice.Broadcast.Loop))
            audio.PlaybackPosition = expected;
    }

    private void ShowCaption(EntityUid grid, Wh40kShipPaBroadcast broadcast)
    {
        _captioned.Add(broadcast.Id);
        _captionOrder.Enqueue(broadcast.Id);
        while (_captionOrder.Count > 256)
            _captioned.Remove(_captionOrder.Dequeue());

        if (_lastCaption == broadcast.Caption && _timing.RealTime - _lastCaptionAt < TimeSpan.FromSeconds(10))
            return;

        _lastCaption = broadcast.Caption;
        _lastCaptionAt = _timing.RealTime;

        if (_subtitle == null)
        {
            _subtitle = new Label
            {
                HorizontalAlignment = Control.HAlignment.Center,
                VerticalAlignment = Control.VAlignment.Top,
                MouseFilter = Control.MouseFilterMode.Ignore,
                FontColorShadowOverride = Color.Black,
                ShadowOffsetXOverride = 1,
                ShadowOffsetYOverride = 1,
            };
            var box = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#100D0AE2"),
                BorderColor = Color.FromHex("#79603D"),
                BorderThickness = new Thickness(2),
            };
            box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 16);
            box.SetContentMarginOverride(StyleBox.Margin.Vertical, 8);
            _subtitlePanel = new PanelContainer
            {
                Name = "Wh40kShipPaSubtitle",
                PanelOverride = box,
                Margin = new Thickness(12, 72, 12, 0),
                MouseFilter = Control.MouseFilterMode.Ignore,
            };
            _subtitlePanel.AddChild(_subtitle);
            LayoutContainer.SetAnchorPreset(_subtitlePanel, LayoutContainer.LayoutPreset.CenterTop);
            LayoutContainer.SetGrowHorizontal(_subtitlePanel, LayoutContainer.GrowDirection.Both);
            _ui.PopupRoot.AddChild(_subtitlePanel);
        }

        var title = Loc.GetString("wh40k-ship-pa-subtitle-title", ("ship", Name(grid)));
        _subtitle.Text = title + "\n" + WrapCaption(broadcast.Caption!);
        _subtitle.FontColorOverride = broadcast.Color;
        _subtitlePriority = broadcast.Priority;
        _subtitleUntil = _timing.RealTime + TimeSpan.FromSeconds(8);
    }

    private static string WrapCaption(string text)
    {
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        if (text.Length > 240)
            text = text[..237] + "...";

        var lines = new List<string>();
        while (text.Length > 64)
        {
            var split = text.LastIndexOf(' ', 64, 64);
            if (split <= 0)
                split = 64;
            lines.Add(text[..split]);
            text = text[split..].TrimStart();
        }

        lines.Add(text);
        return string.Join("\n", lines);
    }

    private void StopVoice(Voice voice)
    {
        if (!Exists(voice.Entity) || TerminatingOrDeleted(voice.Entity))
            return;

        if (TryComp(voice.Entity, out AudioComponent? audio))
            audio.StopPlaying();

        Del(voice.Entity);
    }

    private void StopVoices()
    {
        foreach (var voice in _voices)
            StopVoice(voice);
        _voices.Clear();
    }
}
