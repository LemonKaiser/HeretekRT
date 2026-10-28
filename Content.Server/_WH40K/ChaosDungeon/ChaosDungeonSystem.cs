using System.Linq;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Systems;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Physics;
using Content.Shared._WH40K.Bosses;
using Content.Shared._WH40K.ChaosDungeon;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.ChaosDungeon;

/// <summary>Owns the attempt quota and the lifetime of private Chaos dungeon surfaces.</summary>
public sealed partial class ChaosDungeonSystem : EntitySystem
{
    public const string BodyId = "AivoriusBurningWorld";
    public const string DungeonLandingSiteId = "ChaosDungeonPad";
    public static readonly TimeSpan InstanceDuration = TimeSpan.FromMinutes(40);
    public static readonly TimeSpan ExtractionDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RefreshDuration = TimeSpan.FromHours(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private WH40KPlayerCultureManager _culture = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private KoronusPlanetarySystem _planetary = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMindSystem _minds = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    private readonly Dictionary<EntityUid, Entity<ChaosDungeonInstanceComponent>> _instances = new();
    private readonly HashSet<NetUserId> _usedPlayers = new();
    private string _cycle = Guid.NewGuid().ToString("N");
    private TimeSpan _nextUpdate;
    private int _started;
    private TimeSpan? _refreshAt;
    private bool _presentationDirty;

    public int StartedCount => _started;
    public int ActiveInstanceCount => _instances.Count;
    public TimeSpan? RefreshAt => _refreshAt;
    public bool HasFreeSlot => _started +
        _instances.Values.Count(i => i.Comp.State == ChaosDungeonInstanceState.Arriving) < 3;

    public bool CanStart(EntityUid ship) => HasFreeSlot && !_instances.ContainsKey(ship) &&
        (!TryComp<ChaosDungeonShipComponent>(ship, out var participant) || participant.UsedCycle != _cycle);

    public string GetPadName(bool includeRefresh = true)
    {
        var name = Loc.GetString("chaos-dungeon-pad-name", ("count", _started));
        return includeRefresh && _refreshAt is { } refresh
            ? Loc.GetString("chaos-dungeon-pad-refresh-name", ("name", name),
                ("time", ChaosDungeonTiming.FormatCountdown(refresh - _timing.CurTime)))
            : name;
    }

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(KoronusPlanetarySystem));
        InitializeFog();
        SubscribeLocalEvent<PlanetaryLandingCompletedEvent>(OnLanded);
        SubscribeLocalEvent<PlanetaryDepartureCompletedEvent>(OnDeparted);
        SubscribeLocalEvent<PlanetaryTransferCancelledEvent>(ev => CancelPreparedLanding(ev.Ship));
        SubscribeLocalEvent<MobStateChangedEvent>(OnBossStateChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ResetState());
    }

    /// <summary>Called only after the normal planetary approach, access and footprint checks.</summary>
    public bool TryPrepareLanding(EntityUid ship, out EntityUid map,
        out KoronusPlanetaryTransferFailure failure)
    {
        map = default;
        failure = !HasFreeSlot ? KoronusPlanetaryTransferFailure.DungeonFull
            : KoronusPlanetaryTransferFailure.DungeonShipUsed;
        if (!CanStart(ship))
            return false;

        // The pad supports medium/large expedition vessels, including Cobra and Perdition.
        // Capital hulls such as the compact Ark must not bypass that limit merely by fitting.
        if (TryComp<VesselComponent>(ship, out var vessel) &&
            _prototypes.TryIndex(vessel.VesselId, out VesselPrototype? prototype) && prototype.Price >= 1000000)
        {
            failure = KoronusPlanetaryTransferFailure.ShuttleTooLarge;
            return false;
        }

        var crew = GetCrew(ship);
        var eligible = crew.Where(p => !HasParticipated(p.Key)).ToDictionary();
        if (eligible.Count == 0)
        {
            failure = KoronusPlanetaryTransferFailure.DungeonPlayerUsed;
            return false;
        }

        if (_planetary.CreateDungeonSurface() is not { } surface)
        {
            failure = KoronusPlanetaryTransferFailure.InvalidTarget;
            return false;
        }

        var instance = EnsureComp<ChaosDungeonInstanceComponent>(surface);
        instance.MapId = Comp<MapComponent>(surface).MapId;
        instance.SourceShip = ship;
        var transform = Transform(ship);
        instance.SourceMapId = transform.MapID;
        instance.SourcePosition = _transform.GetWorldPosition(transform);
        instance.SourceAngle = _transform.GetWorldRotation(transform);
        foreach (var participant in eligible)
            instance.Participants.Add(participant.Key, participant.Value);
        if (!PrepareArena((surface, instance)))
        {
            _maps.DeleteMap(instance.MapId);
            failure = KoronusPlanetaryTransferFailure.InvalidTarget;
            return false;
        }

        _instances.Add(ship, (surface, instance));
        _presentationDirty = true;
        // Reject individual repeat visitors before their ship begins its descent.
        foreach (var (user, body) in crew)
        {
            if (!eligible.ContainsKey(user))
                MovePlayer(body, new EntityCoordinates(transform.MapUid!.Value, instance.SourcePosition));
        }
        map = surface;
        failure = KoronusPlanetaryTransferFailure.None;
        return true;
    }

    public void CancelPreparedLanding(EntityUid ship)
    {
        if (_instances.TryGetValue(ship, out var instance) &&
            instance.Comp.State == ChaosDungeonInstanceState.Arriving)
            DeleteInstance(instance);
    }

    private void OnLanded(PlanetaryLandingCompletedEvent ev)
    {
        if (!_instances.TryGetValue(ev.Ship, out var instance) || instance.Comp.MapId != ev.Surface ||
            instance.Comp.State != ChaosDungeonInstanceState.Arriving)
            return;

        // Crew may change during the launch startup. Admit only bodies that actually arrived;
        // reservations in other descents also count when rejecting a repeat visitor.
        instance.Comp.Participants.Clear();
        foreach (var (user, body) in GetCrew(ev.Ship))
        {
            if (HasParticipated(user))
            {
                if (_maps.TryGetMap(instance.Comp.SourceMapId, out var source))
                    MovePlayer(body, new EntityCoordinates(source.Value, instance.Comp.SourcePosition));
                continue;
            }
            instance.Comp.Participants.Add(user, body);
        }
        RefreshAttempts();
        instance.Comp.State = ChaosDungeonInstanceState.Active;
        instance.Comp.Slot = ++_started;
        instance.Comp.ExpiresAt = _timing.CurTime + InstanceDuration;
        _refreshAt ??= _timing.CurTime + RefreshDuration;
        var ship = EnsureComp<ChaosDungeonShipComponent>(ev.Ship);
        ship.UsedCycle = _cycle;
        ship.ReturnPending = true;
        _usedPlayers.UnionWith(instance.Comp.Participants.Keys);
        SetParticipantTimers(instance.Comp, instance.Comp.ExpiresAt);
        Announce(instance.Comp, "chaos-dungeon-started", ("minutes", (int) InstanceDuration.TotalMinutes));
        _presentationDirty = true;
    }

    private bool HasParticipated(NetUserId user)
    {
        return _usedPlayers.Contains(user) || _instances.Values.Any(i =>
            i.Comp.State == ChaosDungeonInstanceState.Arriving && i.Comp.Participants.ContainsKey(user));
    }

    private void OnDeparted(PlanetaryDepartureCompletedEvent ev)
    {
        if (!_instances.TryGetValue(ev.Ship, out var instance))
            return;
        if (instance.Comp.State == ChaosDungeonInstanceState.Active)
        {
            instance.Comp.State = ChaosDungeonInstanceState.Escaped;
            FinishInstance(instance, ChaosDungeonInstanceState.Escaped);
        }
        // After victory the remaining five-minute evacuation window stays open for stragglers.
    }

    private void OnBossStateChanged(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
            return;
        foreach (var instance in _instances.Values)
        {
            if (instance.Comp.Boss != ev.Target || instance.Comp.State != ChaosDungeonInstanceState.Active)
                continue;
            CompleteArena(instance);
            break;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromMilliseconds(250);
        RefreshAttempts();

        foreach (var instance in _instances.Values.ToArray())
        {
            var data = instance.Comp;
            if (TerminatingOrDeleted(instance.Owner))
            {
                _instances.Remove(data.SourceShip);
                if (TryComp<ChaosDungeonShipComponent>(data.SourceShip, out var ship))
                    ship.ReturnPending = false;
                continue;
            }
            if (data.State == ChaosDungeonInstanceState.Arriving)
            {
                if (TerminatingOrDeleted(data.SourceShip) ||
                    !HasComp<Content.Server._WH40K.SectorMap.Components.KoronusPlanetaryTransitComponent>(data.SourceShip))
                    CancelPreparedLanding(data.SourceShip);
                continue;
            }
            if (data.State == ChaosDungeonInstanceState.Completed)
            {
                RemoveFog(data);
                UpdateExtraction(instance);
                continue;
            }
            if (data.State == ChaosDungeonInstanceState.Escaped)
            {
                FinishInstance(instance, ChaosDungeonInstanceState.Escaped);
                continue;
            }
            if (data.State != ChaosDungeonInstanceState.Active)
                continue;
            // Launch has physically left the surface; its atmospheric sequence still needs to finish.
            if (TryComp<Content.Server._WH40K.SectorMap.Components.KoronusPlanetaryTransitComponent>(
                    data.SourceShip, out var transit) && !transit.Landing && transit.EnteredTransitSpace)
                continue;
            if (_timing.CurTime >= data.ExpiresAt || !HasLivingParticipants(data))
            {
                FinishInstance(instance, ChaosDungeonInstanceState.Failed);
                continue;
            }
            if (data.BossSpawnAt is { } spawnAt && _timing.CurTime >= spawnAt)
            {
                data.BossSpawnAt = null;
                if (!TerminatingOrDeleted(data.BossSpawnGate))
                    SpawnArenaBoss(instance, data.BossSpawnGate);
            }
            if (!data.TimeoutWarningSent && data.ExpiresAt - _timing.CurTime <= ExtractionDuration)
            {
                data.TimeoutWarningSent = true;
                Announce(data, "chaos-dungeon-timeout-warning", ("minutes", (int) ExtractionDuration.TotalMinutes));
            }
        }

        if (_presentationDirty)
        {
            _presentationDirty = false;
            EntityManager.System<ShuttleConsoleSystem>().RefreshShuttleConsoles();
        }
    }

    private bool HasLivingParticipants(ChaosDungeonInstanceComponent instance)
    {
        // Critical and disconnected bodies are still alive. Only death (or deletion) removes a participant.
        return instance.Participants.Values.Any(body => !TerminatingOrDeleted(body) &&
            HasComp<MobStateComponent>(body) && !_mobState.IsDead(body));
    }

    private void RefreshAttempts()
    {
        if (_refreshAt is not { } deadline || _timing.CurTime < deadline)
            return;
        _started = 0;
        _refreshAt = null;
        _cycle = Guid.NewGuid().ToString("N");
        _usedPlayers.Clear();
        _presentationDirty = true;
    }

    private void SetParticipantTimers(ChaosDungeonInstanceComponent instance, TimeSpan deadline, bool extraction = false)
    {
        foreach (var body in instance.Participants.Values)
        {
            if (TerminatingOrDeleted(body))
                continue;
            var timer = EnsureComp<ChaosDungeonTimerComponent>(body);
            timer.ExpiresAt = deadline;
            timer.Extraction = extraction;
            Dirty(body, timer);
        }
    }

    private void Announce(ChaosDungeonInstanceComponent instance, string key, params (string, object)[] arguments)
    {
        foreach (var user in instance.Participants.Keys)
        {
            if (!_players.TryGetSessionById(user, out var session))
                continue;
            var message = _culture.GetPlayerString(session, key, arguments);
            _chat.ChatMessageToOne(ChatChannel.Server, message, message, EntityUid.Invalid, false, session.Channel);
        }
    }

    private void UpdateExtraction(Entity<ChaosDungeonInstanceComponent> instance)
    {
        var data = instance.Comp;
        var remaining = data.ExtractionAt - _timing.CurTime;
        if (remaining <= TimeSpan.Zero)
        {
            FinishInstance(instance, ChaosDungeonInstanceState.Extracted);
            return;
        }
        var minute = (int) Math.Ceiling(remaining.TotalMinutes);
        if (minute == data.LastWarningMinute)
            return;
        data.LastWarningMinute = minute;
        Announce(data, "chaos-dungeon-extraction-warning", ("minutes", minute));
    }

    private void FinishInstance(Entity<ChaosDungeonInstanceComponent> instance, ChaosDungeonInstanceState result)
    {
        var data = instance.Comp;
        if (TerminatingOrDeleted(data.SourceShip) || !TryFindReturnTile(data.SourceShip, out var destination))
        {
            // A damaged ship must never cause survivors to be deleted with their map.
            // Keep the instance alive until a return tile can be found.
            return;
        }
        var previous = data.State;
        data.State = result;
        if ((Transform(data.SourceShip).MapID == data.MapId ||
            TryComp<Content.Server._WH40K.SectorMap.Components.KoronusPlanetaryTransitComponent>(
                data.SourceShip, out var transit) && transit.SourceMap == data.MapId) &&
            !_planetary.ReturnDungeonShip(data))
        {
            data.State = previous;
            return;
        }
        foreach (var body in data.Participants.Values)
        {
            if (!TerminatingOrDeleted(body) && Transform(body).MapID == data.MapId && !_mobState.IsDead(body))
                MovePlayer(body, destination);
        }
        // Include player bodies that arrived by another means, including disconnected players.
        // No living player may be deleted along with the private surface.
        var minds = AllEntityQuery<MindComponent>();
        while (minds.MoveNext(out var mind))
        {
            if (mind.UserId != null && mind.OwnedEntity is { } body && !TerminatingOrDeleted(body) &&
                Transform(body).MapID == data.MapId && !_mobState.IsDead(body))
                MovePlayer(body, destination);
        }
        RemoveDeadParticipants(instance, removeShipCorpses: result != ChaosDungeonInstanceState.Extracted);
        Announce(data, result == ChaosDungeonInstanceState.Extracted
            ? "chaos-dungeon-extracted" : "chaos-dungeon-failed");
        DeleteInstance(instance);
    }

    private bool TryFindReturnTile(EntityUid ship, out EntityCoordinates destination)
    {
        destination = default;
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return false;
        var tiles = _maps.GetAllTilesEnumerator(ship, grid);
        while (tiles.MoveNext(out var tile))
        {
            if (tile is not { } candidate || candidate.Tile.IsEmpty || _turf.IsSpace(candidate) ||
                _turf.IsTileBlocked(candidate, CollisionGroup.MobMask))
                continue;
            destination = _maps.ToCenterCoordinates(candidate, grid);
            return true;
        }
        return false;
    }

    private void RemoveDeadParticipants(Entity<ChaosDungeonInstanceComponent> instance, bool removeShipCorpses)
    {
        foreach (var (user, body) in instance.Comp.Participants)
        {
            // Only this attempt's bodies and minds are affected, including disconnected players.
            if (TerminatingOrDeleted(body) || !_mobState.IsDead(body) ||
                Transform(body).MapID != instance.Comp.MapId &&
                (!removeShipCorpses || Transform(body).GridUid != instance.Comp.SourceShip))
                continue;
            if (_minds.TryGetMind(user, out var mind, out var mindComp) && mindComp.OwnedEntity == body)
            {
                if (_players.TryGetSessionById(user, out var session))
                    _ticker.ReturnPlayerToLobby(session);
                else
                    _minds.WipeMind(mind, mindComp);
            }
            Del(body);
        }
    }

    private Dictionary<NetUserId, EntityUid> GetCrew(EntityUid ship)
    {
        var result = new Dictionary<NetUserId, EntityUid>();
        var minds = AllEntityQuery<MindComponent>();
        while (minds.MoveNext(out var mind))
        {
            if (mind.UserId is { } user && mind.OwnedEntity is { } body && !TerminatingOrDeleted(body) &&
                Transform(body).GridUid == ship)
                result[user] = body;
        }
        return result;
    }

    private void MovePlayer(EntityUid body, EntityCoordinates destination)
    {
        if (TryComp<BuckleComponent>(body, out var buckle) && buckle.Buckled)
            _buckle.Unbuckle((body, buckle), null);
        if (TryComp<PullableComponent>(body, out var pullable))
            _pulling.TryStopPull(body, pullable);
        if (TryComp<PullerComponent>(body, out var puller) && puller.Pulling is { } pulled &&
            TryComp<PullableComponent>(pulled, out var target))
            _pulling.TryStopPull(pulled, target);
        if (_containers.TryGetContainingContainer(body, out var container))
            _containers.Remove(body, container, force: true, destination: destination);
        else
            _transform.SetCoordinates(body, destination);
        if (TryComp<PhysicsComponent>(body, out var physics))
        {
            _physics.SetLinearVelocity(body, System.Numerics.Vector2.Zero, body: physics);
            _physics.SetAngularVelocity(body, 0f, body: physics);
        }
    }

    private void DeleteInstance(Entity<ChaosDungeonInstanceComponent> instance)
    {
        RemoveFog(instance.Comp);
        foreach (var body in instance.Comp.Participants.Values)
        {
            if (!TerminatingOrDeleted(body))
                RemComp<ChaosDungeonTimerComponent>(body);
        }
        _instances.Remove(instance.Comp.SourceShip);
        _presentationDirty = true;
        if (TryComp<ChaosDungeonShipComponent>(instance.Comp.SourceShip, out var ship))
            ship.ReturnPending = false;
        if (_maps.MapExists(instance.Comp.MapId))
            _maps.DeleteMap(instance.Comp.MapId);
    }

    private void ResetState()
    {
        _instances.Clear();
        _cycle = Guid.NewGuid().ToString("N");
        _usedPlayers.Clear();
        _started = 0;
        _refreshAt = null;
        _nextUpdate = default;
        _presentationDirty = false;
    }
}
