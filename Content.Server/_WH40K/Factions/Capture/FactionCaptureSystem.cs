using System.Linq;
using Content.Server._Mono.Company;
using Content.Server._NF.Bank;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Factions;
using Content.Shared._WH40K.Factions.Capture;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Factions.Capture;

public sealed partial class FactionCaptureSystem : EntitySystem
{
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private CompanyManager _companies = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(0.5);
    private static readonly Color Contested = Color.FromHex("#EE804A");
    private readonly Dictionary<EntityUid, Site> _sites = new();
    private readonly Dictionary<NetUserId, string> _windowAffiliations = new();
    private readonly Queue<(string Faction, FactionBankEntry Entry)> _pendingRewards = new();
    private readonly FactionCaptureStreaks _streaks = new();
    private TimeSpan _interval;
    private TimeSpan _window;
    private TimeSpan _hold;
    private TimeSpan _nextActivation;
    private TimeSpan? _windowEnd;
    private TimeSpan _lastSample;
    private TimeSpan _nextUi;
    private TimeSpan _nextRewardRetry;
    private int _nextSiteNumber;
    private bool _awarding;

    public override void Initialize()
    {
        SubscribeLocalEvent<FactionCapturePointComponent, MapInitEvent>(OnPointInit);
        SubscribeLocalEvent<FactionCapturePointComponent, ComponentShutdown>(OnPointRemoved);
        SubscribeLocalEvent<FactionCapturePointComponent, BoundUIOpenedEvent>(OnPointUiOpened);
        SubscribeLocalEvent<FactionCaptureTerminalComponent, BoundUIOpenedEvent>(OnTerminalUiOpened);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ResetSchedule());
        ResetSchedule();
    }

    internal void ResetSchedule()
    {
        var interval = Math.Clamp(_cfg.GetCVar(FactionCaptureCVars.Interval), 3, 86400);
        var window = Math.Clamp(_cfg.GetCVar(FactionCaptureCVars.Window), 2, interval - 1);
        _interval = TimeSpan.FromSeconds(interval);
        _window = TimeSpan.FromSeconds(window);
        _hold = TimeSpan.FromSeconds(Math.Clamp(_cfg.GetCVar(FactionCaptureCVars.Hold), 1, window - 1));
        _nextActivation = _timing.CurTime + _interval;
        _windowEnd = null;
        _lastSample = _timing.CurTime;
        _windowAffiliations.Clear();
        _streaks.Reset();
        _nextSiteNumber = 0;
        foreach (var site in _sites.Values)
        {
            site.Number = ++_nextSiteNumber;
            site.Progress.Reset(clearOwner: true);
            UpdateVisual(site);
        }
    }

    private void OnPointInit(Entity<FactionCapturePointComponent> ent, ref MapInitEvent args)
    {
        var transform = Transform(ent);
        ent.Comp.Radius = float.IsFinite(ent.Comp.Radius) ? Math.Clamp(ent.Comp.Radius, 2f, 24f) : 9f;
        var site = new Site(ent, ++_nextSiteNumber, transform.MapID, transform.GridUid, transform.LocalPosition);
        _sites.Add(ent, site);
        UpdateVisual(site);
        Dirty(ent, ent.Comp);
    }

    private void OnPointRemoved(Entity<FactionCapturePointComponent> ent, ref ComponentShutdown args)
        => _sites.Remove(ent);

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (_windowEnd is { } end && end <= now)
            CloseWindow();

        if (_nextActivation <= now)
        {
            var first = _nextActivation;
            var skipped = (now - first).Ticks / _interval.Ticks;
            if (skipped > 0)
                _streaks.Reset();

            var start = first + _interval * skipped;
            _nextActivation = start + _interval;
            if (start + _window > now)
                OpenWindow(start + _window, now);
            else
                _streaks.Reset();
        }

        if (_windowEnd != null && now - _lastSample >= SampleInterval)
        {
            SamplePoints(now - _lastSample > SampleInterval * 2 ? TimeSpan.Zero : now - _lastSample);
            _lastSample = now;
        }

        if (now >= _nextUi)
        {
            _nextUi = now + TimeSpan.FromSeconds(1);
            UpdateOpenTerminals();
        }

        if (!_awarding && _pendingRewards.Count != 0 && now >= _nextRewardRetry)
            _ = DeliverRewardAsync();
    }

    private void OpenWindow(TimeSpan endsAt, TimeSpan now)
    {
        _windowEnd = endsAt;
        _lastSample = now;
        _windowAffiliations.Clear();
        foreach (var site in _sites.Values)
        {
            site.Progress.Reset(clearOwner: true);
            UpdateVisual(site);
        }
    }

    private void CloseWindow()
    {
        _windowEnd = null;
        foreach (var site in _sites.Values.OrderBy(site => site.Number))
        {
            if (IsInstalled(site) && site.Progress.Owner is { } faction && IsValidFaction(faction))
            {
                var amount = _streaks.Award(faction);
                var entry = new FactionBankEntry(Guid.NewGuid(), FactionBankEntryKind.CaptureReward,
                    Guid.Empty, Guid.Empty, SiteName(site) ?? $"#{site.Number}", amount, amount, string.Empty, DateTime.UtcNow);
                _pendingRewards.Enqueue((faction, entry));
            }

            site.Progress.Reset(clearOwner: false);
            UpdateVisual(site);
        }
        _streaks.CloseWindow();
    }

    private async System.Threading.Tasks.Task DeliverRewardAsync()
    {
        _awarding = true;
        try
        {
            var (faction, entry) = _pendingRewards.Peek();
            if (await _bank.CreditCaptureRewardAsync(faction, entry))
                _pendingRewards.Dequeue();
            else
                _nextRewardRetry = _timing.CurTime + TimeSpan.FromSeconds(30);
        }
        finally
        {
            _awarding = false;
        }
    }

    private void SamplePoints(TimeSpan elapsed)
    {
        foreach (var site in _sites.Values)
        {
            site.Factions.Clear();
            site.HasCapturePresence = false;
            site.Installed = IsInstalled(site);
            if (site.Installed)
                site.Position = _transform.GetWorldPosition(site.Point.Owner);
        }

        if (_sites.Count > 0 && _companies.Ready.IsCompletedSuccessfully)
        {
            foreach (var session in _players.Sessions)
            {
                if (session.Status != SessionStatus.InGame || !session.Channel.AuthType.HasStaticUserId() ||
                    session.AttachedEntity is not { } actor || Paused(actor) || HasComp<GhostComponent>(actor) ||
                    !_mobs.IsAlive(actor) || _containers.IsEntityOrParentInContainer(actor) ||
                    !TryComp(actor, out CompanyComponent? company) || !IsValidFaction(company.CompanyName.Id) ||
                    !_companies.IsMember(session.UserId, company.CompanyName))
                    continue;

                var faction = company.CompanyName.Id;
                if (_windowAffiliations.TryGetValue(session.UserId, out var locked) && locked != faction)
                    continue;

                if (RecordPresence(actor, faction) is { } site)
                {
                    _windowAffiliations.TryAdd(session.UserId, faction);
                    site.HasCapturePresence = true;
                }
            }
        }

        foreach (var site in _sites.Values)
        {
            if (site.Progress.Owner is { } owner && !IsValidFaction(owner))
                site.Progress.Reset(clearOwner: true);

            // Overlapping zones still see both sides. One player can advance only their nearest point.
            if (site.Factions.Count == 1 && !site.HasCapturePresence)
                site.Factions.Clear();

            site.Progress.Sample(site.Factions, elapsed, _hold);
            UpdateVisual(site);
        }
    }

    private Site? RecordPresence(EntityUid actor, string faction)
    {
        var position = _transform.GetWorldPosition(actor);
        var xform = Transform(actor);
        Site? closest = null;
        var min = float.MaxValue;
        foreach (var site in _sites.Values)
        {
            if (!site.Installed || xform.MapID != site.Map || xform.GridUid != site.Grid)
                continue;

            var distance = System.Numerics.Vector2.DistanceSquared(position, site.Position);
            if (distance > site.Point.Comp.Radius * site.Point.Comp.Radius)
                continue;

            site.Factions.Add(faction);
            if (distance > min ||
                distance == min && closest != null && closest.Number < site.Number)
                continue;

            min = distance;
            closest = site;
        }
        return closest;
    }

    private bool IsInstalled(Site site)
    {
        var (uid, _) = site.Point;
        return site.Grid != null && TryComp(uid, out TransformComponent? xform) && xform.Anchored &&
               xform.MapID == site.Map && xform.GridUid == site.Grid && xform.LocalPosition == site.LocalPosition;
    }

    private bool IsValidFaction(string faction) => faction != "None" &&
        _prototypes.TryIndex<CompanyPrototype>(faction, out var company) && !company.Disabled;

    private void UpdateVisual(Site site)
    {
        var point = site.Point.Comp;
        var status = GetStatus(site);
        var faction = status switch
        {
            FactionCaptureStatus.Capturing => site.Progress.Challenger,
            FactionCaptureStatus.Held => site.Progress.Owner,
            _ => null,
        };
        var color = status == FactionCaptureStatus.Contested ? Contested : faction != null &&
            _prototypes.TryIndex<CompanyPrototype>(faction, out var company) ? company.Color : FactionCapturePointComponent.NeutralColor;
        var progress = status switch
        {
            FactionCaptureStatus.Capturing or FactionCaptureStatus.Contested => (float) (site.Progress.Elapsed / _hold),
            FactionCaptureStatus.Held => 1f,
            _ => 0f,
        };
        if (point.Status == status && point.BoundaryColor == color && point.CaptureProgress == progress)
            return;

        point.Status = status;
        point.BoundaryColor = color;
        point.CaptureProgress = progress;
        Dirty(site.Point.Owner, point);
    }

    private FactionCaptureStatus GetStatus(Site site)
    {
        if (!IsInstalled(site))
            return FactionCaptureStatus.Offline;

        if (_windowEnd == null)
            return FactionCaptureStatus.Cooldown;

        var progress = site.Progress;
        return progress.Contested ? FactionCaptureStatus.Contested : progress.Challenger != null ? FactionCaptureStatus.Capturing :
            progress.Owner != null ? FactionCaptureStatus.Held : FactionCaptureStatus.Available;
    }

    private string? SiteName(Site site)
    {
        var (uid, point) = site.Point;
        if (!string.IsNullOrWhiteSpace(point.SiteName))
            return point.SiteName;

        var meta = MetaData(uid);
        return meta.EntityName == meta.EntityPrototype?.Name ? null : meta.EntityName;
    }

    private void OnPointUiOpened(Entity<FactionCapturePointComponent> entity, ref BoundUIOpenedEvent args)
        => _ui.SetUiState(entity.Owner, FactionCaptureUiKey.Key, BuildUiState(entity.Owner));

    private void OnTerminalUiOpened(Entity<FactionCaptureTerminalComponent> entity, ref BoundUIOpenedEvent args)
        => _ui.SetUiState(entity.Owner, FactionCaptureUiKey.Key, BuildUiState());

    private void UpdateOpenTerminals()
    {
        FactionCaptureUiState? state = null;
        var terminals = EntityQueryEnumerator<FactionCaptureTerminalComponent>();
        while (terminals.MoveNext(out var terminal, out _))
        {
            if (_ui.IsUiOpen(terminal, FactionCaptureUiKey.Key))
                _ui.SetUiState(terminal, FactionCaptureUiKey.Key, state ??= BuildUiState());
        }

        foreach (var site in _sites.Values)
        {
            var point = site.Point.Owner;
            if (_ui.IsUiOpen(point, FactionCaptureUiKey.Key))
                _ui.SetUiState(point, FactionCaptureUiKey.Key, BuildUiState(point));
        }
    }

    internal FactionCaptureUiState BuildUiState(EntityUid? only = null)
    {
        var rows = new List<FactionCaptureSite>();
        var factionPositions = new Dictionary<string, int>();
        foreach (var site in _sites.Values.OrderBy(site => site.Number))
        {
            var progress = site.Progress;
            var status = GetStatus(site);

            var amount = _streaks.Project(_windowEnd == null ? progress.Owner : null);
            if (status != FactionCaptureStatus.Offline && _windowEnd != null && progress.Owner is { } owner)
            {
                var position = factionPositions.GetValueOrDefault(owner) + 1;
                factionPositions[owner] = position;
                amount = _streaks.Project(owner, position);
            }

            if (only == null || site.Point.Owner == only)
                rows.Add(new FactionCaptureSite(site.Number, SiteName(site), status, progress.Owner, progress.Challenger,
                    Seconds(_hold - progress.Elapsed), amount));
        }

        var now = _timing.CurTime;
        return new FactionCaptureUiState(rows.ToArray(), Seconds(_nextActivation - now),
            _windowEnd is { } end ? Seconds(end - now) : 0, Seconds(_hold));
    }

    private static int Seconds(TimeSpan time) => Math.Max(0, (int) Math.Ceiling(time.TotalSeconds));

    private sealed class Site(Entity<FactionCapturePointComponent> point, int number, MapId map, EntityUid? grid,
        System.Numerics.Vector2 localPosition)
    {
        public readonly Entity<FactionCapturePointComponent> Point = point;
        public int Number = number;
        public readonly MapId Map = map;
        public readonly EntityUid? Grid = grid;
        public readonly System.Numerics.Vector2 LocalPosition = localPosition;
        public System.Numerics.Vector2 Position;
        public bool Installed;
        public bool HasCapturePresence;
        public readonly HashSet<string> Factions = new();
        public readonly FactionCaptureProgress Progress = new();
    }
}
