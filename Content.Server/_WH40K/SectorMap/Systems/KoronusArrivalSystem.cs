using System.Linq;
using Content.Server._WH40K.Activities;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.Chat.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Components;
using Content.Shared.GameTicking.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.SectorMap.Systems;

public sealed partial class KoronusArrivalSystem : EntitySystem
{
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private KoronusSectorResidencySystem _residency = default!;
    [Dependency] private KoronusActivityDirectorSystem _activities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private WH40KPlayerCultureManager _culture = default!;

    private readonly Dictionary<(string System, KoronusArrivalKind Kind), ArrivalState> _states = new();
    private TimeSpan _nextUpdate;
    // The activity director allocates positive lease ids. Arrivals own the negative range.
    private long _nextLeaseId = -1;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<KoronusArrivalGridComponent, GridSplitEvent>(OnGridSplit);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        ClearArrivals();
        _nextUpdate = default;
        _nextLeaseId = -1;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        UpdateArrivals(_timing.CurTime);
    }

    internal void UpdateArrivals(TimeSpan now)
    {
        if (!TryGetSettings(out var rule, out _))
        {
            ClearArrivals();
            return;
        }

        var configurations = _prototypes.Index(rule.Sector).Arrivals
            .Select(id => _prototypes.Index(id)).Where(KoronusArrivalPolicy.IsValid)
            .GroupBy(config => config.Kind).Select(group => group.First()).ToArray();
        foreach (var config in configurations)
        foreach (var system in _prototypes.EnumeratePrototypes<KoronusSystemPrototype>())
        {
            if (system.Sector != rule.Sector ||
                !KoronusArrivalPolicy.IsEligible(system, _prototypes.Index(rule.Sector).StartSystem.Id) ||
                !_sector.TryGetSystemMap(system.ID, out _))
                continue;

            var key = (system.ID, config.Kind);
            if (!_states.TryGetValue(key, out var state))
            {
                state = new ArrivalState { Settings = config };
                ScheduleNext(state, config, now);
                _states.Add(key, state);
            }

            if (state.LeaseId != 0)
            {
                if (state.ArrivalAt is { } arrival && now >= arrival)
                {
                    state.ArrivalAt = null;
                    if (!TrySpawn(system, state))
                        Finish(system.ID, state);
                    else
                        state.DepartureAt = now + TimeSpan.FromSeconds(state.Lifetime);
                }
                if (state.DepartureAt is { } departure && now >= departure)
                    Finish(system.ID, state);
                else if (state.DepartureAt != null && state.Settings.Kind == KoronusArrivalKind.Tau)
                    UpdatePatrol(system, state, now);
                continue;
            }

            if (now >= state.NextAt && !TryStart(system.ID, null, null, now, config.ID))
                ScheduleNext(state, config, now);
        }
    }

    public bool TryStart(string systemId, string? mapPath = null, float? lifetime = null,
        string? templateId = null, string? groupId = null) =>
        TryStart(systemId, mapPath, lifetime, _timing.CurTime, templateId, groupId);

    internal bool TryStart(string systemId, string? mapPath, float? lifetime, TimeSpan now,
        string? templateId = null, string? groupId = null)
    {
        if (!TryGetSettings(out var rule, out var settings, templateId, selectRandom: true, systemId: systemId) ||
            !_sector.TryGetSystemPrototype(systemId, out var system) || system.Sector != rule.Sector ||
            !KoronusArrivalPolicy.IsEligible(system, _prototypes.Index(rule.Sector).StartSystem.Id) ||
            lifetime is { } seconds && (!float.IsFinite(seconds) || seconds <= 0f || seconds > 7200f) ||
            _states.TryGetValue((systemId, settings.Kind), out var existing) && existing.LeaseId != 0)
            return false;

        List<string> paths;
        if (mapPath != null)
            paths = [mapPath];
        else if (settings.Groups.Count > 0)
        {
            var group = groupId == null ? _random.Pick(settings.Groups) : settings.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null || group.Maps.Count == 0)
                return false;
            paths = group.Maps;
        }
        else
        {
            if (settings.Maps.Count == 0)
                return false;
            paths = [_random.Pick(settings.Maps)];
        }

        var normalized = new List<ResPath>();
        foreach (var input in paths)
        {
            if (!KoronusArrivalPolicy.TryGetMapPath(input, out var path) ||
                !(_resources.ContentFileExists(path) || _resources.UserData.Exists(path)))
                return false;
            normalized.Add(path);
        }
        if (!_sector.TryEnsureSystemMapLoaded(systemId, out _))
            return false;

        var leaseId = _nextLeaseId--;
        if (!_residency.BeginActivityLease(systemId, leaseId))
            return false;

        var state = existing ?? new ArrivalState();
        _states[(systemId, settings.Kind)] = state;
        state.Settings = settings;
        state.LeaseId = leaseId;
        state.Paths = normalized;
        state.Lifetime = lifetime ?? _random.NextFloat(settings.MinLifetime, settings.MaxLifetime);
        state.ArrivalAt = now + TimeSpan.FromSeconds(settings.WarningTime);
        ScheduleNext(state, settings, now);
        Announce(systemId, settings);
        return true;
    }

    private bool TryGetSettings(out KoronusSectorRuleComponent rule, out KoronusArrivalPrototype settings,
        string? templateId = null, bool selectRandom = false, string? systemId = null)
    {
        rule = default!;
        settings = default!;
        var query = EntityQueryEnumerator<KoronusSectorRuleComponent, ActiveGameRuleComponent>();
        while (query.MoveNext(out _, out var sector, out _))
        {
            if (sector.WaitingForStartMap)
                continue;
            var candidates = new List<KoronusArrivalPrototype>();
            foreach (var id in _prototypes.Index(sector.Sector).Arrivals)
                if ((templateId == null || id.Id == templateId) && _prototypes.TryIndex(id, out var config) &&
                    KoronusArrivalPolicy.IsValid(config) &&
                    (!selectRandom || systemId == null ||
                     !_states.TryGetValue((systemId, config.Kind), out var running) || running.LeaseId == 0))
                    candidates.Add(config);
            if (candidates.Count == 0)
                continue;
            settings = selectRandom ? _random.Pick(candidates) : candidates[0];
            rule = sector;
            return true;
        }
        return false;
    }

    private void ScheduleNext(ArrivalState state, KoronusArrivalPrototype settings, TimeSpan now)
    {
        state.NextAt = now + TimeSpan.FromSeconds(_random.NextFloat(settings.MinInterval, settings.MaxInterval));
    }

    private bool TrySpawn(KoronusSystemPrototype system, ArrivalState state)
    {
        if (!_sector.TryGetSystemMap(system.ID, out var target) || !_maps.MapExists(target))
            return false;

        var staging = _maps.CreateMap(out var stagingId);
        try
        {
            var grids = new List<Entity<MapGridComponent>>();
            var radius = 0f;
            foreach (var path in state.Paths)
            {
                if (!_loader.TryLoadGrid(stagingId, path, out var grid,
                        new DeserializationOptions { InitializeMaps = true }))
                    return false;
                if (grid.Value.Comp.LocalAABB.Size.LengthSquared() <= 0f)
                    return false;
                grids.Add(grid.Value);
                radius = MathF.Max(radius, grid.Value.Comp.LocalAABB.Size.Length() / 2f);
            }
            // A shared formation keeps a drone patrol together and reserves its whole footprint.
            var spacing = MathF.Max(150f, radius * 2f + 50f);
            radius += (grids.Count - 1) * spacing / 2f;
            if (!_sector.TryGetArrivalPosition(system, target, radius, out var center))
                return false;

            for (var i = 0; i < grids.Count; i++)
            {
                var grid = grids[i];
                var rotation = _transform.GetWorldRotation(grid.Owner);
                var offset = new System.Numerics.Vector2((i - (grids.Count - 1) / 2f) * spacing, 0f);
                _transform.SetMapCoordinates(grid.Owner,
                    new MapCoordinates(center + offset - rotation.RotateVec(grid.Comp.LocalAABB.Center), target));
                EnsureComp<KoronusArrivalGridComponent>(grid.Owner).LeaseId = state.LeaseId;
                state.Grids.Add(grid.Owner);
                ApplyColor(grid.Owner, state.Settings);
                if (state.Settings.Kind == KoronusArrivalKind.Tau)
                    ConfigurePatrol(grid.Owner, system);
            }
            return true;
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to load Koronus arrival in {system.ID}: {exception}");
            return false;
        }
        finally
        {
            Del(staging);
        }
    }

    private void ApplyColor(EntityUid grid, KoronusArrivalPrototype settings)
    {
        _shuttle.SetIFFColor(grid, settings.Color);
        _shuttle.AddIFFFlag(grid, IFFFlags.AlwaysShowColor);
    }

    private void Announce(string systemId, KoronusArrivalPrototype settings)
    {
        if (!_sector.TryGetSystemPrototype(systemId, out var system))
            return;

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } player || !TryComp(player, out TransformComponent? transform))
                continue;

            if (!IsInSystem(transform.MapID, systemId))
                continue;

            using var scope = _culture.CreateScope(session);
            _chat.DispatchFilteredAnnouncement(Filter.SinglePlayer(session),
                Loc.GetString(settings.Announcement, ("system", system.DisplayName)),
                sender: Loc.GetString("koronus-arrival-sender"),
                announcementSound: settings.Sound, colorOverride: settings.Color);
        }
    }

    internal bool IsInSystem(MapId mapId, string systemId)
    {
        if (_sector.TryGetSystemId(mapId, out var id) && id == systemId)
            return true;
        return _maps.TryGetMap(mapId, out var map) &&
            TryComp<KoronusPlanetSurfaceMapComponent>(map.Value, out var surface) && surface.SystemId == systemId;
    }

    private void OnGridSplit(EntityUid uid, KoronusArrivalGridComponent component, ref GridSplitEvent args)
    {
        var state = _states.Values.FirstOrDefault(s => s.LeaseId == component.LeaseId);
        if (state == null)
            return;
        foreach (var grid in args.NewGrids)
        {
            EnsureComp<KoronusArrivalGridComponent>(grid).LeaseId = state.LeaseId;
            state.Grids.Add(grid);
            if (state.Settings.Kind == KoronusArrivalKind.Object)
                ApplyColor(grid, state.Settings);
        }
    }

    private void Finish(string systemId, ArrivalState state)
    {
        foreach (var grid in state.Grids)
        {
            if (TerminatingOrDeleted(grid))
                continue;
            _docking.UndockDocks(grid);
            _activities.EvacuateAttachedPlayers(grid);
            QueueDel(grid);
        }
        state.Grids.Clear();
        state.PatrolOrders.Clear();
        _residency.EndActivityLease(systemId, state.LeaseId);
        state.LeaseId = 0;
        state.ArrivalAt = null;
        state.DepartureAt = null;
    }

    internal void ClearArrivals()
    {
        foreach (var (system, state) in _states)
            Finish(system.System, state);
        _states.Clear();
    }

    internal IReadOnlyDictionary<(string System, KoronusArrivalKind Kind), ArrivalState> States => _states;

    internal sealed class ArrivalState
    {
        public KoronusArrivalPrototype Settings = default!;
        public readonly Dictionary<EntityUid, (EntityCoordinates Destination, TimeSpan ExpiresAt)> PatrolOrders = new();
        public TimeSpan NextAt;
        public TimeSpan? ArrivalAt;
        public TimeSpan? DepartureAt;
        public long LeaseId;
        public float Lifetime;
        public List<ResPath> Paths = new();
        public readonly HashSet<EntityUid> Grids = new();
    }
}
