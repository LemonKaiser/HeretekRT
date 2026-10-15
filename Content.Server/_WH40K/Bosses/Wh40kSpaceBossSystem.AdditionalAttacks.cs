using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Damage.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Content.Shared.NPC;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kSpaceBossSystem
{
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private TriggerSystem _triggers = default!;
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private HTNSystem _htn = default!;

    private void InitializeAdditionalAttacks()
    {
        SubscribeLocalEvent<Wh40kBossMinionComponent, MapInitEvent>(OnSpawnedMapInit);
        SubscribeLocalEvent<Wh40kBossMinionComponent, PreventCollideEvent>(OnMinePreventCollide);
        SubscribeLocalEvent<Wh40kBossMinionComponent, StartCollideEvent>(OnMineCollide);
        SubscribeLocalEvent<Wh40kBossMinionComponent, MeleeHitEvent>(OnMinionMeleeHit);
    }

    private void OnSpawnedMapInit(EntityUid uid, Wh40kBossMinionComponent component, MapInitEvent args)
    {
        if (!component.SporeMine)
            return;

        // Reuse the biological explosive, but deploy it as a drifting contact mine.
        _steering.Unregister(uid);
        RemComp<HTNComponent>(uid);
        RemComp<ActiveNPCComponent>(uid);
        RemComp<NPCMeleeCombatComponent>(uid);
        RemComp<MeleeWeaponComponent>(uid);
        RemComp<DamageOnHitComponent>(uid);
    }

    private bool IsBossAlly(EntityUid boss, EntityUid entity)
    {
        return TryComp<Wh40kBossMinionComponent>(entity, out var spawned) && spawned.Boss == boss ||
               _factions.IsEntityFriendly(boss, entity);
    }

    private void OnMinionMeleeHit(EntityUid uid, Wh40kBossMinionComponent component, MeleeHitEvent args)
    {
        if (args.HitEntities.Any(IsProtectedTarget))
            args.Handled = true;
    }

    private void OnMinePreventCollide(EntityUid uid, Wh40kBossMinionComponent component, ref PreventCollideEvent args)
    {
        if (component.SporeMine && (HasComp<Wh40kSpaceBossComponent>(args.OtherEntity) ||
            _factions.IsEntityFriendly(uid, args.OtherEntity)))
            args.Cancelled = true;
    }

    private void OnMineCollide(EntityUid uid, Wh40kBossMinionComponent component, ref StartCollideEvent args)
    {
        if (!component.SporeMine || component.Detonated || !args.OtherFixture.Hard ||
            TerminatingOrDeleted(uid) || TerminatingOrDeleted(args.OtherEntity) ||
            HasComp<Wh40kSpaceBossComponent>(args.OtherEntity) || _factions.IsEntityFriendly(uid, args.OtherEntity) ||
            IsProtectedTarget(args.OtherEntity) || !TryComp(args.OtherEntity, out TransformComponent? other))
            return;

        var grid = HasComp<MapGridComponent>(args.OtherEntity) ? args.OtherEntity : other.GridUid;
        if (grid is not { } vessel || !_domains.IsVesselGrid(vessel))
            return;

        component.Detonated = true;
        _triggers.Trigger(uid, uid);
    }

    private void UpdateSpawnedCreatures()
    {
        var query = EntityQueryEnumerator<Wh40kBossMinionComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (Paused(uid) || TerminatingOrDeleted(uid))
                continue;
            if (component.Boss is { } boss &&
                (TerminatingOrDeleted(boss) || !_mobState.IsAlive(boss) || IsProtectedTarget(uid)))
            {
                QueueDel(uid);
                continue;
            }

            if (!component.SporeMine && TryComp<HTNComponent>(uid, out var htn) && htn.Plan is { } plan &&
                htn.Blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager) &&
                !CanBoarderTarget(uid, target))
            {
                _htn.ShutdownTask(plan.CurrentOperator, htn.Blackboard, HTNOperatorStatus.Failed);
                _htn.ShutdownPlan(htn);
            }
        }
    }

    public bool CanBoarderTarget(EntityUid uid, EntityUid target)
    {
        return target != uid && !TerminatingOrDeleted(uid) && !TerminatingOrDeleted(target) &&
               !HasComp<GhostComponent>(target) && _mobState.IsAlive(target) &&
               TryComp(uid, out TransformComponent? xform) && xform.GridUid is { } grid &&
               TryComp(target, out TransformComponent? other) && other.GridUid == grid &&
               other.MapID == xform.MapID && _players.Sessions.Any(session => session.AttachedEntity == target) &&
               !IsProtectedTarget(target);
    }

    public async Task<(bool Valid, Dictionary<string, object>? Effects)> PlanBoardingTarget(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (TerminatingOrDeleted(uid) || !TryComp(uid, out TransformComponent? xform))
            return (false, null);

        blackboard.TryGetValue<EntityUid>("Target", out var current, EntityManager);
        var origin = _transform.GetWorldPosition(xform);
        var candidates = _players.Sessions
            .Select(session => session.AttachedEntity)
            .OfType<EntityUid>()
            .Where(target => CanBoarderTarget(uid, target))
            .OrderBy(target => target == current ? -1f :
                Vector2.DistanceSquared(origin, _transform.GetWorldPosition(target)))
            .ToArray();
        var range = blackboard.GetValueOrDefault<float>("MeleeRange", EntityManager);
        var flags = _pathfinding.GetFlags(blackboard);

        foreach (var target in candidates)
        {
            cancelToken.ThrowIfCancellationRequested();
            var path = await _pathfinding.GetPath(uid, target, range, cancelToken, flags);
            if (path.Result != PathResult.Path || !CanBoarderTarget(uid, target))
                continue;

            // MoveTo reuses this route, including the doors that must be pried open.
            return (true, new Dictionary<string, object>
            {
                { "Target", target },
                { "TargetCoordinates", new EntityCoordinates(target, Vector2.Zero) },
                { "TargetPathfind", path },
            });
        }

        return (false, null);
    }

    private int CountSpawned(EntityUid boss, bool mines)
    {
        var count = 0;
        var query = EntityQueryEnumerator<Wh40kBossMinionComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Boss == boss && component.SporeMine == mines && !TerminatingOrDeleted(uid) && !_mobState.IsDead(uid))
                count++;
        }
        return count;
    }

    private void QueueBoardingWave(EntityUid uid, Wh40kSpaceBossComponent component, EntityUid? grid, MapCoordinates point)
    {
        if (component.BoardingPrototype == null || grid is not { } vessel || !_domains.IsVesselGrid(vessel) ||
            IsProtectedTarget(vessel) || _timing.CurTime < component.NextBoarding ||
            CountSpawned(uid, mines: false) >= component.MaxBoarders)
            return;

        // Keep the impact in grid coordinates so the breach follows a moving or rotating ship.
        component.BoardingImpact = _transform.ToCoordinates(vessel, point);
        component.BoardingAt = _timing.CurTime + TimeSpan.FromSeconds(0.1);
        component.NextBoarding = _timing.CurTime + component.BoardingCooldown;
    }

    private void UpdateBoardingWave(EntityUid uid, Wh40kSpaceBossComponent component)
    {
        if (component.BoardingImpact is not { } impact || _timing.CurTime < component.BoardingAt)
            return;
        component.BoardingImpact = null;
        var vessel = impact.EntityId;
        if (component.BoardingPrototype is not { } prototype || TerminatingOrDeleted(vessel) ||
            !TryComp<MapGridComponent>(vessel, out var grid) || IsProtectedTarget(vessel))
            return;

        // Damage alone does not guarantee a breach: a surviving hull must not admit boarders.
        if (!_maps.TryGetTileRef(vessel, grid, impact, out var breach) ||
            _turf.IsTileBlocked(breach, CollisionGroup.MobMask))
            return;

        var breachPosition = _transform.ToMapCoordinates(impact);
        var count = Math.Min(component.BoardingCount, component.MaxBoarders - CountSpawned(uid, mines: false));
        for (var i = 0; i < count; i++)
        {
            for (var attempt = 0; attempt < 32; attempt++)
            {
                var candidate = impact.Offset(_random.NextVector2(component.BoardingRadius));
                if (!_maps.TryGetTileRef(vessel, grid, candidate, out var tile) || _turf.IsSpace(tile))
                    continue;
                var centre = _turf.GetTileCenter(tile);
                if (Vector2.DistanceSquared(centre.Position, impact.Position) > component.BoardingRadius * component.BoardingRadius ||
                    _turf.IsTileBlocked(tile, CollisionGroup.MobMask))
                    continue;

                var offset = _transform.ToMapCoordinates(centre).Position - breachPosition.Position;
                if (offset.LengthSquared() > 0.0001f &&
                    _physics.IntersectRay(breachPosition.MapId,
                        new CollisionRay(breachPosition.Position, offset.Normalized(), (int) CollisionGroup.MobMask),
                        offset.Length(), uid, false).Any())
                    continue;

                var creature = SpawnAtPosition(prototype, centre);
                EnsureComp<Wh40kBossMinionComponent>(creature).Boss = uid;
                break;
            }
        }
    }

    private void UpdateSporeMines(EntityUid uid, Wh40kSpaceBossComponent component, TransformComponent xform)
    {
        if (component.SporeMinePrototype is not { } prototype || xform.MapUid is not { } map ||
            _timing.CurTime < component.NextSpore || IsProtectedTarget(uid))
            return;

        component.NextSpore = _timing.CurTime + component.SporeInterval;
        if (_physics.GetMapLinearVelocity(uid, xform: xform).LengthSquared() < 25 ||
            CountSpawned(uid, mines: true) >= Math.Min(100, component.MaxSporeMines))
            return;

        var origin = _transform.GetWorldPosition(xform);
        var facing = _transform.GetWorldRotation(xform);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var angle = facing + new Angle(_random.NextFloat(MathF.PI / 2, MathF.PI * 1.5f));
            var position = origin + angle.ToVec() * _random.NextFloat(20f, 28f);
            if (!IsClearFlightPath(uid, xform, position))
                continue;

            var mine = SpawnAtPosition(prototype, new EntityCoordinates(map, position));
            Comp<Wh40kBossMinionComponent>(mine).Boss = uid;
            _physics.SetLinearVelocity(mine, angle.ToVec() * 2f);
            return;
        }
    }
}
