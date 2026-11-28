using Content.Server.Chat.Systems;
using Content.Shared._WH40K.ShipPa;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.ShipPa;

/// <summary>
/// Manages a vessel's grid-wide Vox-PA network: announcements, klaxon loops, and automatic speaker coverage
/// (including air alarm fallback when no dedicated speakers are installed).
/// </summary>
public sealed partial class Wh40kShipPaSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private ChatSystem _chat = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.25);
    private static readonly SoundSpecifier DefaultChime = new SoundPathSpecifier("/Audio/Announcements/attention.ogg");

    private readonly HashSet<EntityUid> _pendingCounts = new();
    private readonly List<Entity<Wh40kShipPaSpeakerComponent>> _speakerBuffer = new();

    private TimeSpan _nextUpdate;
    private int _nextBroadcastId = 1;

    public override void Initialize()
    {
        base.Initialize();
        InitializeSpeakers();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        ClearExpiredBroadcasts();
        FlushPendingCounts();
        UpdateBroadcasts();
    }

    public int? Broadcast(
        EntityUid grid,
        SoundSpecifier sound,
        AudioParams? audioParams = null,
        int priority = Wh40kShipPaPlaybackPolicy.AnnouncementPriority,
        string key = "announcement")
    {
        if (!Exists(grid) || CountSpeakers(grid).Online == 0)
            return null;

        return StartBroadcast(grid, key, sound, false, Wh40kShipPaBroadcastKind.Announcement, priority, audioParams)?.Id;
    }

    public bool Announce(
        EntityUid grid,
        string message,
        SoundSpecifier? sound = null,
        string? sender = null,
        Color? color = null)
    {
        if (!Exists(grid) || CountSpeakers(grid).Online == 0)
            return false;

        var chime = sound ?? CompOrNull<Wh40kShipAlertComponent>(grid)?.AnnouncementChime ?? DefaultChime;
        if (StartBroadcast(
                grid,
                "announcement",
                chime,
                false,
                Wh40kShipPaBroadcastKind.Announcement,
                Wh40kShipPaPlaybackPolicy.AnnouncementPriority,
                caption: message,
                color: color) == null)
        {
            return false;
        }

        _chat.DispatchFilteredAnnouncement(
            GetListeners(grid),
            message,
            source: grid,
            sender: sender ?? GetShipName(grid),
            playSound: false,
            announcementSound: null,
            colorOverride: color);
        return true;
    }

    public Filter GetListeners(EntityUid grid)
    {
        var filter = Filter.Empty();

        foreach (var speaker in GetSpeakers(grid))
        {
            if (IsFunctional(speaker))
                filter.AddInRange(_xform.GetMapCoordinates(speaker.Owner), speaker.Comp.Range);
        }

        return filter;
    }

    public List<Entity<Wh40kShipPaSpeakerComponent>> GetSpeakers(EntityUid grid)
    {
        var speakers = new List<Entity<Wh40kShipPaSpeakerComponent>>();
        GatherSpeakers(grid, speakers);
        return speakers;
    }

    public bool IsFunctional(Entity<Wh40kShipPaSpeakerComponent> speaker)
    {
        if (speaker.Comp.Broken || TerminatingOrDeleted(speaker.Owner))
            return false;

        if (!TryComp(speaker.Owner, out TransformComponent? xform) || !xform.Anchored || xform.GridUid == null)
            return false;

        return IsSpeakerPowered(speaker.Owner);
    }

    public (int Online, int Total, bool Fallback) CountSpeakers(EntityUid grid)
    {
        var online = 0;
        var total = 0;
        var fallback = false;

        foreach (var speaker in GetSpeakers(grid))
        {
            total++;
            fallback |= speaker.Comp.Fallback;

            if (IsFunctional(speaker))
                online++;
        }

        return (online, total, fallback);
    }

    public void RefreshCounts(EntityUid grid)
    {
        if (!Exists(grid) || TerminatingOrDeleted(grid))
            return;

        RefreshCoverage(grid);
        if (TryComp(grid, out Wh40kShipPaBroadcastComponent? broadcast))
            UpdateBroadcastLights(grid, broadcast);

        var (online, total, fallback) = CountSpeakers(grid);

        if (!TryComp(grid, out Wh40kShipAlertComponent? alert))
        {
            if (total == 0 || fallback)
                return;

            alert = EnsureComp<Wh40kShipAlertComponent>(grid);
        }

        if (alert.SpeakersOnline == online && alert.SpeakersTotal == total && alert.SpeakersFallback == fallback)
            return;

        alert.SpeakersOnline = online;
        alert.SpeakersTotal = total;
        alert.SpeakersFallback = fallback;
        Dirty(grid, alert);
    }

    public string GetShipName(EntityUid grid)
    {
        if (TryComp(grid, out MetaDataComponent? meta) && !string.IsNullOrWhiteSpace(meta.EntityName))
            return meta.EntityName;

        return Loc.GetString("wh40k-ship-pa-unknown-ship");
    }

    private void GatherSpeakers(EntityUid grid, List<Entity<Wh40kShipPaSpeakerComponent>> into)
    {
        if (!Exists(grid))
            return;

        var first = into.Count;
        var dedicated = false;
        if (!_gridSpeakers.TryGetValue(grid, out var members))
            return;

        foreach (var uid in members)
        {
            if (!TryComp(uid, out Wh40kShipPaSpeakerComponent? speaker) || TerminatingOrDeleted(uid)
                || !TryComp(uid, out TransformComponent? xform) || !xform.Anchored || xform.GridUid != grid)
                continue;

            dedicated |= !speaker.Fallback;
            into.Add((uid, speaker));
        }

        if (!dedicated)
            return;

        for (var i = into.Count - 1; i >= first; i--)
        {
            if (into[i].Comp.Fallback)
                into.RemoveAt(i);
        }
    }

    private void RefreshCoverage(EntityUid grid)
    {
        if (!_gridSpeakers.TryGetValue(grid, out var members))
            return;

        var dedicated = false;
        foreach (var uid in members)
        {
            if (TryComp(uid, out Wh40kShipPaSpeakerComponent? speaker) && !speaker.Fallback && !TerminatingOrDeleted(uid))
                dedicated = true;
        }

        foreach (var uid in members)
        {
            if (!TryComp(uid, out Wh40kShipPaSpeakerComponent? speaker) || TerminatingOrDeleted(uid))
                continue;

            var enabled = (!dedicated || !speaker.Fallback) && IsFunctional((uid, speaker));
            if (speaker.Enabled == enabled)
                continue;

            speaker.Enabled = enabled;
            Dirty(uid, speaker);
        }
    }

    private int NextBroadcastId() => _nextBroadcastId++;

    private void QueueRefresh(EntityUid? grid)
    {
        if (grid is { } uid && uid.IsValid())
            _pendingCounts.Add(uid);
    }

    private void FlushPendingCounts()
    {
        if (_pendingCounts.Count == 0)
            return;

        foreach (var grid in _pendingCounts)
            RefreshCounts(grid);

        _pendingCounts.Clear();
    }
}
