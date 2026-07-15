using System.Linq;
using System.Numerics;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server._WH40K.SectorMap.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared._WH40K.SectorMap.Teleporters;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// Server-authoritative personnel transfer between a surface and shuttles in that planet's orbital
/// system. A source handles one mob at a time and revalidates power, range, target and deed access
/// both before and after the five-second spin-up.
/// </summary>
public sealed class KoronusPlanetaryTeleporterSystem : EntitySystem
{
    private const float UseRadius = 0.7f;
    private static readonly TimeSpan SpinupTime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ArrivalCooldown = TimeSpan.FromSeconds(2);

    private readonly SoundSpecifier _spinSound = new SoundPathSpecifier("/Audio/_WH40K/Effects/teleporter_spin.wav");
    private readonly SoundSpecifier _sendSound = new SoundPathSpecifier("/Audio/_WH40K/Effects/teleporter_send.wav");
    private readonly SoundSpecifier _receiveSound = new SoundPathSpecifier("/Audio/_WH40K/Effects/teleporter_receive.wav");

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private ShuttleConsoleLockSystem _shipAccess = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private KoronusPlanetarySystem _planetary = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;

    private TimeSpan _nextScan;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KoronusPlanetaryTeleporterComponent, AfterActivatableUIOpenEvent>(OnUiOpened);
        SubscribeLocalEvent<KoronusPlanetaryTeleporterComponent, KoronusPlanetaryTeleporterSelectMessage>(OnSelect);
        SubscribeLocalEvent<KoronusPlanetaryTeleporterComponent, KoronusPlanetaryTeleporterAccessMessage>(OnAccess);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextScan)
            return;

        _nextScan = _timing.CurTime + TimeSpan.FromMilliseconds(100);
        var query = EntityQueryEnumerator<KoronusPlanetaryTeleporterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var component, out var transform))
            UpdateTeleporter((uid, component, transform));
    }

    private void UpdateTeleporter(Entity<KoronusPlanetaryTeleporterComponent, TransformComponent> entity)
    {
        if (entity.Comp1.ActiveUser is { } active)
        {
            if (!CanContinue(entity, active, out var target))
            {
                Cancel(entity);
                return;
            }

            if (_timing.CurTime < entity.Comp1.CompleteAt)
                return;

            Complete(entity, active, target);
            return;
        }

        if (!IsOperational(entity.Owner, entity.Comp2) || string.IsNullOrEmpty(entity.Comp1.SelectedTarget))
            return;

        var mobs = new HashSet<Entity<MobStateComponent>>();
        _lookup.GetEntitiesInRange(entity.Comp2.Coordinates, UseRadius, mobs, flags: LookupFlags.Uncontained);
        foreach (var mob in mobs.OrderBy(candidate => candidate.Owner.Id))
        {
            if (!_mobState.IsAlive(mob.Owner, mob.Comp) ||
                TryComp<KoronusTeleporterArrivalCooldownComponent>(mob.Owner, out var cooldown) &&
                _timing.CurTime < cooldown.Until ||
                !TryResolveTarget(entity.Owner, entity.Comp1, mob.Owner, out _))
            {
                continue;
            }

            entity.Comp1.ActiveUser = mob.Owner;
            entity.Comp1.CompleteAt = _timing.CurTime + SpinupTime;
            _audio.PlayPvs(_spinSound, entity.Owner);
            UpdateUiState((entity.Owner, entity.Comp1));
            break;
        }
    }

    private bool CanContinue(
        Entity<KoronusPlanetaryTeleporterComponent, TransformComponent> entity,
        EntityUid user,
        out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (TerminatingOrDeleted(user) || !IsOperational(entity.Owner, entity.Comp2))
            return false;

        var userPosition = _transform.GetWorldPosition(Transform(user));
        var teleporterPosition = _transform.GetWorldPosition(entity.Comp2);
        return Vector2.DistanceSquared(userPosition, teleporterPosition) <= UseRadius * UseRadius &&
               TryResolveTarget(entity.Owner, entity.Comp1, user, out target);
    }

    private void Complete(
        Entity<KoronusPlanetaryTeleporterComponent, TransformComponent> entity,
        EntityUid user,
        EntityUid target)
    {
        var targetTransform = Transform(target);
        if (TryGetSurfaceRuntime(targetTransform.MapID, out _))
            _maps.SetPaused(targetTransform.MapID, false);

        _audio.PlayPvs(_sendSound, entity.Owner);
        _audio.PlayPvs(_receiveSound, target);
        _transform.SetCoordinates(user, Transform(user), targetTransform.Coordinates);
        var cooldown = EnsureComp<KoronusTeleporterArrivalCooldownComponent>(user);
        cooldown.Until = _timing.CurTime + ArrivalCooldown;
        _adminLog.Add(LogType.Teleport,
            $"{ToPrettyString(user)} teleported from {ToPrettyString(entity.Owner)} to {ToPrettyString(target)}");
        Cancel(entity);
    }

    private void Cancel(Entity<KoronusPlanetaryTeleporterComponent, TransformComponent> entity)
    {
        entity.Comp1.ActiveUser = null;
        entity.Comp1.CompleteAt = default;
        UpdateUiState((entity.Owner, entity.Comp1));
    }

    private bool TryResolveTarget(
        EntityUid source,
        KoronusPlanetaryTeleporterComponent component,
        EntityUid user,
        out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (component.SelectedTarget == null || !CanUseSource(source, component, user))
            return false;

        if (TryGetSurfaceRuntime(Transform(source).MapID, out var surfaceRuntime))
        {
            var candidates = GetShuttleTeleporters(surfaceRuntime.SystemId, component.SelectedTarget, user);
            if (candidates.Count == 0)
                return false;

            target = _random.Pick(candidates);
            return true;
        }

        var sourceGrid = Transform(source).GridUid;
        if (sourceGrid == null || !HasComp<ShuttleComponent>(sourceGrid.Value))
            return false;

        foreach (var candidate in GetSurfaceTeleporters(sourceGrid.Value, user))
        {
            if (candidate.Id != component.SelectedTarget)
                continue;

            target = candidate.Teleporter;
            return candidate.Available;
        }

        return false;
    }

    private bool CanUseSource(EntityUid source, KoronusPlanetaryTeleporterComponent component, EntityUid user)
    {
        if (component.PublicAccess)
            return true;

        if (Transform(source).GridUid is { } grid && HasComp<ShuttleComponent>(grid))
            return _shipAccess.HasShipAccess(grid, user);

        if (TryGetSurfaceRuntime(Transform(source).MapID, out var surface) && component.SelectedTarget != null)
            return GetShuttleGrid(surface.SystemId, component.SelectedTarget, out var shuttle) &&
                   _shipAccess.HasShipAccess(shuttle, user);

        return false;
    }

    private bool IsOperational(EntityUid uid, TransformComponent? transform = null)
    {
        return Resolve(uid, ref transform, false) && transform.Anchored &&
               (!TryComp<ApcPowerReceiverComponent>(uid, out var receiver) ||
                !receiver.PowerDisabled && (!receiver.NeedsPower || _power.IsPowered(uid, receiver)));
    }

    private void OnUiOpened(
        Entity<KoronusPlanetaryTeleporterComponent> entity,
        ref AfterActivatableUIOpenEvent args)
    {
        UpdateUiState(entity);
        SendTargets(entity, args.Actor);
    }

    private void OnSelect(
        Entity<KoronusPlanetaryTeleporterComponent> entity,
        ref KoronusPlanetaryTeleporterSelectMessage args)
    {
        if (entity.Comp.ActiveUser != null)
            return;

        var targetId = args.TargetId;
        var targets = BuildTargetStates(entity.Owner, entity.Comp, args.Actor);
        if (!targets.Any(target => target.Id == targetId && target.Available))
            return;

        entity.Comp.SelectedTarget = targetId;
        UpdateUiState(entity);
        SendTargets(entity, args.Actor);
    }

    private void OnAccess(
        Entity<KoronusPlanetaryTeleporterComponent> entity,
        ref KoronusPlanetaryTeleporterAccessMessage args)
    {
        if (entity.Comp.Locked || entity.Comp.ActiveUser != null || !MayConfigure(entity.Owner, args.Actor))
            return;

        entity.Comp.PublicAccess = args.PublicAccess;
        UpdateUiState(entity);
        SendTargets(entity, args.Actor);
    }

    private bool MayConfigure(EntityUid teleporter, EntityUid user)
    {
        if (Transform(teleporter).GridUid is not { } grid || !HasComp<ShuttleComponent>(grid))
            return true;

        return _shipAccess.HasShipAccess(grid, user);
    }

    private void UpdateUiState(Entity<KoronusPlanetaryTeleporterComponent> entity)
    {
        _ui.SetUiState(entity.Owner, KoronusPlanetaryTeleporterUiKey.Key,
            new KoronusPlanetaryTeleporterState(
                TryGetSurfaceRuntime(Transform(entity).MapID, out _),
                entity.Comp.PublicAccess,
                entity.Comp.Locked,
                IsOperational(entity.Owner),
                entity.Comp.ActiveUser != null));
    }

    private void SendTargets(Entity<KoronusPlanetaryTeleporterComponent> entity, EntityUid actor)
    {
        _ui.ServerSendUiMessage(
            entity.Owner,
            KoronusPlanetaryTeleporterUiKey.Key,
            new KoronusPlanetaryTeleporterTargetsMessage(BuildTargetStates(entity.Owner, entity.Comp, actor)),
            actor);
    }

    private List<KoronusPlanetaryTeleporterTargetState> BuildTargetStates(
        EntityUid source,
        KoronusPlanetaryTeleporterComponent component,
        EntityUid actor)
    {
        if (TryGetSurfaceRuntime(Transform(source).MapID, out var surfaceRuntime))
        {
            var result = new List<KoronusPlanetaryTeleporterTargetState>();
            foreach (var grid in GetShuttleGrids(surfaceRuntime.SystemId))
            {
                var id = ShuttleId(grid);
                var available = _shipAccess.HasShipAccess(grid, actor) &&
                                GetOperationalTeleporters(grid).Count > 0;
                var name = MetaData(grid).EntityName;
                if (TryComp<ShuttleDeedComponent>(grid, out var deed) && !string.IsNullOrWhiteSpace(deed.ShuttleName))
                    name = deed.ShuttleName;
                result.Add(new KoronusPlanetaryTeleporterTargetState(
                    id,
                    name,
                    available,
                    component.SelectedTarget == id));
            }

            return result.OrderBy(target => target.Name, StringComparer.Ordinal).ToList();
        }

        if (Transform(source).GridUid is not { } sourceGrid)
            return new List<KoronusPlanetaryTeleporterTargetState>();

        return GetSurfaceTeleporters(sourceGrid, actor)
            .Select(candidate => new KoronusPlanetaryTeleporterTargetState(
                candidate.Id,
                MetaData(candidate.Teleporter).EntityName,
                candidate.Available,
                component.SelectedTarget == candidate.Id))
            .OrderBy(target => target.Name, StringComparer.Ordinal)
            .ToList();
    }

    private List<SurfaceTeleporterTarget> GetSurfaceTeleporters(EntityUid shuttleGrid, EntityUid user)
    {
        var result = new List<SurfaceTeleporterTarget>();
        if (!_sector.TryGetSystemId(Transform(shuttleGrid).MapID, out var systemId))
            return result;

        // Destination surfaces are deliberately paused while empty. They must remain selectable
        // from orbit; Complete() unpauses the chosen surface immediately before moving the mob.
        var query = EntityManager.AllEntityQueryEnumerator<KoronusPlanetaryTeleporterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var component, out var transform))
        {
            if (!TryGetSurfaceRuntime(transform.MapID, out var surfaceRuntime) ||
                surfaceRuntime.SystemId != systemId ||
                !TryResolveBodyForSurface(surfaceRuntime.SurfaceId, out var body) ||
                !_planetary.IsWithinLandingApproach(shuttleGrid, body.ID) ||
                !IsOperational(uid, transform))
            {
                continue;
            }

            var available = component.PublicAccess || _shipAccess.HasShipAccess(shuttleGrid, user);
            result.Add(new SurfaceTeleporterTarget(uid, TeleporterId(uid), available));
        }

        return result;
    }

    private List<EntityUid> GetShuttleTeleporters(string systemId, string shuttleId, EntityUid user)
    {
        if (!GetShuttleGrid(systemId, shuttleId, out var grid) || !_shipAccess.HasShipAccess(grid, user))
            return new List<EntityUid>();

        return GetOperationalTeleporters(grid);
    }

    private List<EntityUid> GetOperationalTeleporters(EntityUid grid)
    {
        var result = new List<EntityUid>();
        var query = EntityQueryEnumerator<KoronusPlanetaryTeleporterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.GridUid == grid && IsOperational(uid, transform))
                result.Add(uid);
        }

        return result;
    }

    private IEnumerable<EntityUid> GetShuttleGrids(string systemId)
    {
        if (!_sector.TryGetSystemMap(systemId, out var orbitalMap))
            yield break;

        var query = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.MapID == orbitalMap && GetOperationalTeleporters(uid).Count > 0)
                yield return uid;
        }
    }

    private bool GetShuttleGrid(string systemId, string id, out EntityUid shuttle)
    {
        foreach (var candidate in GetShuttleGrids(systemId))
        {
            if (ShuttleId(candidate) != id)
                continue;

            shuttle = candidate;
            return true;
        }

        shuttle = EntityUid.Invalid;
        return false;
    }

    private bool TryGetSurfaceRuntime(MapId mapId, out KoronusPlanetSurfaceMapComponent runtime)
    {
        if (_maps.TryGetMap(mapId, out var mapUid) &&
            TryComp<KoronusPlanetSurfaceMapComponent>(mapUid.Value, out var foundRuntime))
        {
            runtime = foundRuntime;
            return true;
        }

        runtime = default!;
        return false;
    }

    private bool TryResolveBodyForSurface(string surfaceId, out KoronusCelestialBodyPrototype body)
    {
        foreach (var candidate in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (candidate.Surface == null || candidate.Surface.Value != surfaceId)
                continue;

            body = candidate;
            return true;
        }

        body = default!;
        return false;
    }

    private static string TeleporterId(EntityUid uid) => $"teleporter:{uid}";
    private static string ShuttleId(EntityUid uid) => $"shuttle:{uid}";

    public bool SetSelectedTarget(EntityUid teleporter, string targetId)
    {
        if (!TryComp<KoronusPlanetaryTeleporterComponent>(teleporter, out var component) ||
            component.ActiveUser != null || string.IsNullOrWhiteSpace(targetId))
        {
            return false;
        }

        component.SelectedTarget = targetId;
        UpdateUiState((teleporter, component));
        return true;
    }

    private readonly record struct SurfaceTeleporterTarget(EntityUid Teleporter, string Id, bool Available);
}
