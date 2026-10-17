using System.Linq;
using System.Numerics;
using Content.Server._Mono.Radar;
using Content.Server._Mono.FireControl;
using Content.Server._WH40K.OperationalDomain;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared._Crescent.ShipShields;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Tiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using static Content.Shared.Damage.DamageableSystem;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kSpaceBossSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private OperationalDomainSystem _domains = default!;
    [Dependency] private KoronusSafetyPolicySystem _safety = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DockingSystem _docking = default!;
    private readonly HashSet<EntityUid> _nearby = new();
    private readonly HashSet<EntityUid> _checkedDockGrids = new();
    private readonly Stack<EntityUid> _dockGrids = new();
    private TimeSpan _nextThink;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kSpaceBossComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<Wh40kSpaceBossComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Wh40kSpaceBossComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<Wh40kSpaceBossComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<FireControllableComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<Wh40kSpaceBossComponent, MeleeHitEvent>(OnBiteDamage);
        SubscribeLocalEvent<Wh40kSpaceBossComponent, MeleeDamageAppliedEvent>(OnBiteApplied);
        InitializeAdditionalAttacks();
    }

    private void OnAmmoShot(EntityUid uid, FireControllableComponent component, AmmoShotEvent args)
    {
        if (!IsProtectedTarget(uid) && !IsProtectedDamageSource(args.User))
            return;

        foreach (var projectile in args.FiredProjectiles)
        {
            if (!TerminatingOrDeleted(projectile))
                EnsureComp<Wh40kBossProtectedShotComponent>(projectile);
        }
    }

    private void OnBeforeDamageChanged(EntityUid uid, Wh40kSpaceBossComponent component, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || args.Damage.GetTotal() <= 0)
            return;

        if (IsProtectedDamageSource(args.Origin) || IsProtectedDamageSource(args.Tool))
            args.Cancelled = true;
    }

    public bool IsProtectedDamageSource(EntityUid? source)
    {
        if (source is not { Valid: true } uid || TerminatingOrDeleted(uid))
            return false;

        if (HasComp<Wh40kBossProtectedShotComponent>(uid) || IsProtectedTarget(uid))
            return true;

        if (TryComp<ProjectileComponent>(uid, out var projectile) &&
            (projectile.Weapon is { } weapon && !TerminatingOrDeleted(weapon) && IsProtectedTarget(weapon) ||
             projectile.Shooter is { } shooter && !TerminatingOrDeleted(shooter) && IsProtectedTarget(shooter)))
            return true;

        return _safety.TryResolveResponsiblePlayer(uid, out var player) && IsProtectedTarget(player);
    }

    private void Stop(EntityUid uid)
    {
        _steering.Unregister(uid);
        RemComp<ActiveNPCComponent>(uid);
        if (TryComp<RadarBlipComponent>(uid, out var blip))
            blip.Enabled = false;
    }

    private void OnShutdown(EntityUid uid, Wh40kSpaceBossComponent component, ComponentShutdown args)
    {
        Stop(uid);
    }

    private void OnMobState(EntityUid uid, Wh40kSpaceBossComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Stop(uid);
        if (args.NewMobState != MobState.Dead || component.DeathHandled)
            return;

        component.DeathHandled = true;
        if (component.DeathLoot is { } loot && TryComp(uid, out TransformComponent? xform) && xform.MapUid is { } map)
            SpawnAtPosition(loot, new EntityCoordinates(map, _transform.GetWorldPosition(xform)));
        if (component.DeleteOnDeath)
            QueueDel(uid);
    }

    private void OnDamageChanged(EntityUid uid, Wh40kSpaceBossComponent component, DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null || !_mobState.IsAlive(uid) || Paused(uid))
            return;

        if (_timing.CurTime > component.PressureUntil)
            component.PressureDamage = 0;
        component.PressureUntil = _timing.CurTime + TimeSpan.FromSeconds(2);
        component.PressureDamage += MathF.Max(0, args.DamageDelta.GetTotal().Float());

        if (TryComp(args.Origin, out TransformComponent? source))
        {
            component.ThreatPosition = _transform.GetWorldPosition(source);
            foreach (var session in _players.Sessions)
            {
                if (session.AttachedEntity is not { } player || !TryComp(player, out TransformComponent? playerTransform) ||
                    (player != args.Origin && (source.GridUid == null || source.GridUid != playerTransform.GridUid)))
                    continue;

                component.RetaliationTarget = player;
                component.RetaliationUntil = _timing.CurTime + TimeSpan.FromSeconds(10);
                if (CanTarget(uid, player, component))
                    break;
                component.RetaliationTarget = null;
            }
        }
    }

    public bool IsProtectedTarget(EntityUid target)
    {
        if (!TryComp(target, out TransformComponent? xform))
            return true;

        var grid = HasComp<MapGridComponent>(target) ? target : xform.GridUid;
        if (_safety.HasRule(target, KoronusSafetyRule.StationProtection | KoronusSafetyRule.HostileNpcAutoCleanup))
            return true;
        if (grid is not { } gridUid)
            return false;

        // Docked vessels share the station's protection, including chains through another vessel.
        _checkedDockGrids.Clear();
        _dockGrids.Clear();
        _dockGrids.Push(gridUid);
        while (_dockGrids.TryPop(out var connected))
        {
            if (!_checkedDockGrids.Add(connected) || TerminatingOrDeleted(connected))
                continue;
            if (HasComp<ProtectedGridComponent>(connected) || HasComp<KoronusProtectedFacilityComponent>(connected) ||
                !_domains.IsVesselGrid(connected) ||
                _safety.HasRule(connected, KoronusSafetyRule.StationProtection | KoronusSafetyRule.HostileNpcAutoCleanup))
                return true;
            foreach (var dock in _docking.GetDocks(connected))
            {
                if (TryComp(dock.Comp.DockedWith, out TransformComponent? other) && other.GridUid is { } otherGrid)
                    _dockGrids.Push(otherGrid);
            }
        }
        return false;
    }

    public bool CanTarget(EntityUid boss, EntityUid player, Wh40kSpaceBossComponent component)
    {
        if (TerminatingOrDeleted(player) || HasComp<GhostComponent>(player) || !_mobState.IsAlive(player) ||
            IsProtectedTarget(player) || !TryComp(player, out TransformComponent? targetTransform))
            return false;

        var bossTransform = Transform(boss);
        var range = player == component.RetaliationTarget && _timing.CurTime < component.RetaliationUntil
            ? component.RetaliationRadius
            : component.DetectionRadius;
        return bossTransform.MapID != MapId.Nullspace && bossTransform.MapID == targetTransform.MapID &&
               Vector2.DistanceSquared(_transform.GetWorldPosition(bossTransform), _transform.GetWorldPosition(targetTransform)) <=
               range * range;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var think = _timing.CurTime >= _nextThink;
        if (think)
        {
            _nextThink = _timing.CurTime + TimeSpan.FromSeconds(0.1);
            UpdateSpawnedCreatures();
        }

        var query = EntityQueryEnumerator<Wh40kSpaceBossComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var component, out var xform))
        {
            if (Paused(uid) || !_mobState.IsAlive(uid) || xform.MapUid is not { } map)
                continue;

            component.Home ??= new EntityCoordinates(map, _transform.GetWorldPosition(xform));
            EnsureComp<ActiveNPCComponent>(uid);
            if (think)
            {
                Think(uid, component, xform);
                UpdateBoardingWave(uid, component);
                UpdateSporeMines(uid, component, xform);
            }

            if (component.Destination is not { } destination)
                continue;

            var turnTo = component.LookPosition ?? _transform.ToMapCoordinates(destination).Position;
            var direction = turnTo - _transform.GetWorldPosition(xform);
            if (direction.LengthSquared() < 0.01f)
                continue;

            // The organism has a head: turn gradually before its jaws can bite.
            var wanted = direction.ToAngle();
            var current = _transform.GetWorldRotation(xform);
            var difference = Angle.ShortestDistance(current, wanted);
            var wantedSpeed = (float) Math.Clamp(difference.Theta * 4, -component.TurnSpeed, component.TurnSpeed);
            component.TurnVelocity += Math.Clamp(wantedSpeed - component.TurnVelocity,
                -component.TurnAcceleration * frameTime, component.TurnAcceleration * frameTime);
            var step = component.TurnVelocity * frameTime;
            if (Math.Abs(step) > Math.Abs(difference.Theta))
            {
                step = (float) difference.Theta;
                component.TurnVelocity = 0;
            }
            var rotation = current + step;
            _transform.SetWorldRotation(uid, rotation);
        }
    }

    private void Think(EntityUid uid, Wh40kSpaceBossComponent component, TransformComponent xform)
    {
        var origin = _transform.GetWorldPosition(xform);
        component.EvadedProjectiles.RemoveWhere(projectile => TerminatingOrDeleted(projectile));
        if (_timing.CurTime >= component.RetaliationUntil)
            component.RetaliationTarget = null;
        EntityUid? target = null;
        var distance = float.MaxValue;
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } player || !CanTarget(uid, player, component))
                continue;
            var candidateDistance = Vector2.DistanceSquared(origin, _transform.GetWorldPosition(player));
            if (player == component.Target)
                candidateDistance *= 0.8f;
            if (player == component.RetaliationTarget)
                candidateDistance *= 0.1f;
            if (candidateDistance >= distance)
                continue;
            distance = candidateDistance;
            target = player;
        }
        if (component.Target != target)
        {
            // Changing the selected passenger on the same ship must not cancel a withdrawal.
            var sameVessel = TryComp(component.Target, out TransformComponent? previous) &&
                             previous.GridUid != null && TryComp(target, out TransformComponent? next) &&
                             previous.GridUid == next.GridUid;
            if (!sameVessel)
            {
                ClearDestination(uid, component);
                component.Phase = Wh40kSpaceBossPhase.Approach;
                component.PhaseUntil = TimeSpan.Zero;
                component.AttackRunUntil = TimeSpan.Zero;
                component.ManeuverUntil = TimeSpan.Zero;
                component.AttackCommitUntil = TimeSpan.Zero;
                component.CommitNextApproach = false;
            }
        }
        component.Target = target;

        if (_timing.CurTime < component.ManeuverUntil && component.Destination is { } maneuver &&
            Vector2.DistanceSquared(origin, _transform.ToMapCoordinates(maneuver).Position) > 9)
        {
            if (IsClearFlightPath(uid, xform, _transform.ToMapCoordinates(maneuver).Position))
                return;
            ClearDestination(uid, component);
            component.ManeuverUntil = TimeSpan.Zero;
        }

        // Finish recovery and flanking, then honour the attack window instead of chaining dodges.
        var mayEvade = (target == null || component.Phase == Wh40kSpaceBossPhase.Approach &&
                        _timing.CurTime >= component.AttackCommitUntil) && _timing.CurTime >= component.NextEvasion;
        EntityUid? threatProjectile = null;
        var threatPosition = component.PressureDamage >= component.EvasionDamageThreshold && _timing.CurTime < component.PressureUntil
            ? component.ThreatPosition
            : null;
        if (mayEvade && TryGetIncomingThreat(uid, component, xform, out var incomingPosition, out threatProjectile))
            threatPosition = incomingPosition;
        if (mayEvade && threatPosition is { } threat && TryManeuver(uid, component, xform, threat, evade: true))
        {
            if (threatProjectile is { } projectile)
                component.EvadedProjectiles.Add(projectile);
            component.PressureDamage = 0;
            component.NextEvasion = _timing.CurTime + TimeSpan.FromSeconds(6);
            if (target != null)
            {
                // A defensive break also ends the current attack run before another approach.
                component.Phase = Wh40kSpaceBossPhase.Retreat;
                component.PhaseUntil = component.ManeuverUntil;
                component.AttackRunUntil = TimeSpan.Zero;
                component.AttackDirection = (threat - origin).Normalized();
                component.CommitNextApproach = true;
            }
            return;
        }

        if (target is not { } enemy)
        {
            Roam(uid, component, xform);
            return;
        }

        var targetTransform = Transform(enemy);
        if (UpdateCombatManeuver(uid, component, xform, targetTransform))
            return;

        var delta = _transform.GetWorldPosition(targetTransform) - origin;
        var length = delta.Length();
        if (length < 0.01f)
            return;
        var direction = delta / length;
        // Select only the first obstruction. A bite cannot tunnel through the hull to its passengers.
        var hit = _physics.IntersectRay(xform.MapID,
            new CollisionRay(origin, direction, (int) (CollisionGroup.Impassable | CollisionGroup.BulletImpassable)),
            length, uid, false).Where(result => !HasComp<ShipShieldComponent>(result.HitEntity) && !IsBossAlly(uid, result.HitEntity))
            .OrderBy(result => result.Distance).FirstOrDefault();
        var attackTarget = hit.HitEntity == EntityUid.Invalid ? enemy : hit.HitEntity;
        var point = hit.HitEntity == EntityUid.Invalid ? _transform.GetWorldPosition(targetTransform) : hit.HitPos;
        var contactDistance = Vector2.Distance(origin, point);
        if (contactDistance > component.HeadDistance + component.RetreatDistance)
            component.AttackRunUntil = TimeSpan.Zero;
        else if (component.AttackRunUntil == TimeSpan.Zero)
            component.AttackRunUntil = _timing.CurTime + TimeSpan.FromSeconds(component.AttackRunDuration);
        else if (_timing.CurTime >= component.AttackRunUntil)
        {
            BeginRetreat(uid, component, xform, direction);
            return;
        }
        if (hit.HitEntity != EntityUid.Invalid && (IsProtectedTarget(attackTarget) ||
            Transform(attackTarget).GridUid != targetTransform.GridUid || !HasComp<DamageableComponent>(attackTarget)))
        {
            if (TryManeuver(uid, component, xform, point, evade: false))
                return;
            component.LookPosition = null;
            Roam(uid, component, xform);
            return;
        }

        component.LookPosition = point;
        var facing = _transform.GetWorldRotation(xform).ToVec();
        var head = origin + facing * component.HeadDistance;
        if (Vector2.Dot(facing, direction) >= 0.9f &&
            Vector2.DistanceSquared(head, point) <= component.BiteReach * component.BiteReach &&
            CanTarget(uid, enemy, component) && !IsProtectedTarget(attackTarget) &&
            TryComp<MeleeWeaponComponent>(uid, out var weapon))
        {
            // Bite before planning the next movement: grid edges must not prevent
            // an already reachable hull from being attacked. Only applied damage starts retreat.
            _combat.SetInCombatMode(uid, true);
            _melee.AttemptLightAttack(uid, uid, weapon, attackTarget);
            if (component.Phase == Wh40kSpaceBossPhase.Retreat)
                return;
        }

        var speed = TryComp<MovementSpeedModifierComponent>(uid, out var movement) ? movement.WeightlessSprintSpeed : 28f;
        var leadTime = MathF.Min(component.PursuitLeadTime, MathF.Max(0, length - component.HeadDistance - 4) / MathF.Max(1, speed));
        var velocity = _physics.GetMapLinearVelocity(enemy, xform: targetTransform);
        var destination = point + velocity * leadTime - direction * (component.HeadDistance + 1f);
        if (_safety.HasRule(xform.MapID, destination, KoronusSafetyRule.StationProtection | KoronusSafetyRule.HostileNpcAutoCleanup) ||
            _maps.TryFindGridAt(new MapCoordinates(destination, xform.MapID), out _, out _))
            destination = point - direction * (component.HeadDistance + 1f);
        if (!IsClearFlightPath(uid, xform, destination))
        {
            if (TryManeuver(uid, component, xform, point, evade: false))
                return;
            component.Destination = null;
            _steering.Unregister(uid);
            Roam(uid, component, xform);
            return;
        }
        SetDestination(uid, component, xform.MapUid!.Value, destination);
    }

    private void ClearDestination(EntityUid uid, Wh40kSpaceBossComponent component)
    {
        component.Destination = null;
        component.LookPosition = null;
        _steering.Unregister(uid);
    }

    private void BeginRetreat(EntityUid uid, Wh40kSpaceBossComponent component, TransformComponent xform,
        Vector2 direction)
    {
        ClearDestination(uid, component);
        component.Phase = Wh40kSpaceBossPhase.Retreat;
        component.PhaseUntil = _timing.CurTime + TimeSpan.FromSeconds(component.RepositionDuration);
        component.AttackRunUntil = TimeSpan.Zero;
        component.ManeuverUntil = TimeSpan.Zero;
        component.AttackCommitUntil = TimeSpan.Zero;
        component.AttackDirection = direction;
        var side = new Vector2(-direction.Y, direction.X) * component.AttackSide;
        // Prefer a diagonal withdrawal; fall back to backing away or either side in confined space.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var away = attempt switch
            {
                0 => -direction + side * 0.3f,
                1 => -direction - side * 0.3f,
                2 => -direction,
                3 => side,
                _ => -side,
            };
            var position = _transform.GetWorldPosition(xform) + away.Normalized() * component.RetreatDistance;
            if (!IsClearFlightPath(uid, xform, position))
                continue;
            SetDestination(uid, component, xform.MapUid!.Value, position);
            component.LookPosition = position;
            return;
        }
        // If surrounded, wait out recovery instead of repeatedly biting the same hull segment.
    }

    private bool UpdateCombatManeuver(EntityUid uid, Wh40kSpaceBossComponent component, TransformComponent xform,
        TransformComponent targetTransform)
    {
        if (component.Phase == Wh40kSpaceBossPhase.Approach)
            return false;

        var origin = _transform.GetWorldPosition(xform);
        var destination = component.Destination;
        var reached = destination is { } goal &&
                      Vector2.DistanceSquared(origin, _transform.ToMapCoordinates(goal).Position) <= 9;
        if (!reached && _timing.CurTime < component.PhaseUntil)
        {
            if (destination is { } flight && !IsClearFlightPath(uid, xform, _transform.ToMapCoordinates(flight).Position))
                ClearDestination(uid, component);
            else if (component.Phase == Wh40kSpaceBossPhase.Flank)
                component.LookPosition = _transform.GetWorldPosition(targetTransform);
            return true;
        }

        ClearDestination(uid, component);
        if (component.Phase == Wh40kSpaceBossPhase.Retreat)
        {
            var side = new Vector2(-component.AttackDirection.Y, component.AttackDirection.X) * component.AttackSide;
            component.AttackSide = -component.AttackSide;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var position = origin + side * component.FlankDistance;
                if (IsClearFlightPath(uid, xform, position))
                {
                    SetDestination(uid, component, xform.MapUid!.Value, position);
                    component.LookPosition = _transform.GetWorldPosition(targetTransform);
                    component.Phase = Wh40kSpaceBossPhase.Flank;
                    component.PhaseUntil = _timing.CurTime + TimeSpan.FromSeconds(component.RepositionDuration);
                    return true;
                }
                side = -side;
            }
        }

        component.Phase = Wh40kSpaceBossPhase.Approach;
        component.PhaseUntil = TimeSpan.Zero;
        component.AttackRunUntil = TimeSpan.Zero;
        if (component.CommitNextApproach)
        {
            component.AttackCommitUntil = _timing.CurTime + TimeSpan.FromSeconds(component.AttackRunDuration);
            component.CommitNextApproach = false;
        }
        return false;
    }

    private void SetDestination(EntityUid uid, Wh40kSpaceBossComponent component, EntityUid map, Vector2 position)
    {
        var destination = new EntityCoordinates(map, position);
        component.Destination = destination;
        // Direct movement can update its goal without cancelling and restarting steering every think.
        var steering = TryComp<NPCSteeringComponent>(uid, out var existing)
            ? existing
            : _steering.Register(uid, destination);
        steering.Coordinates = destination;
        steering.DirectMove = true;
        steering.Status = SteeringStatus.Moving;
        steering.Range = 2f;
        steering.InRangeMaxSpeed = 2f;
        // Avoidance measures fixture surfaces, so using the entire body length here prevented approach.
        steering.Radius = component.AvoidanceRadius;
    }

    private bool TryGetIncomingThreat(EntityUid uid, Wh40kSpaceBossComponent component, TransformComponent xform,
        out Vector2? threat, out EntityUid? threatProjectile)
    {
        threat = null;
        threatProjectile = null;
        _nearby.Clear();
        _lookup.GetEntitiesInRange(uid, 80f, _nearby, LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Sensors);
        var position = _transform.GetWorldPosition(xform);
        var velocity = _physics.GetMapLinearVelocity(uid, xform: xform);
        var soonest = 1.25f;
        foreach (var entity in _nearby)
        {
            if (component.EvadedProjectiles.Contains(entity) || !TryComp<ProjectileComponent>(entity, out var projectile) ||
                projectile.ProjectileSpent || projectile.Shooter == uid ||
                projectile.Damage.GetTotal() <= 0 || IsProtectedDamageSource(entity) || !TryComp(entity, out TransformComponent? projectileTransform))
                continue;
            var projectileVelocity = _physics.GetMapLinearVelocity(entity, xform: projectileTransform);
            var incoming = projectileVelocity - velocity;
            var offset = _transform.GetWorldPosition(projectileTransform) - position;
            if (incoming.LengthSquared() < 1 || Vector2.Dot(offset, projectileVelocity) >= 0)
                continue;
            var time = -Vector2.Dot(offset, incoming) / incoming.LengthSquared();
            if (time < 0 || time > soonest ||
                (offset + incoming * time).LengthSquared() > MathF.Pow(component.HeadDistance + 3f, 2))
                continue;
            soonest = time;
            threat = position - incoming.Normalized() * 80f;
            threatProjectile = entity;
        }
        return threat != null;
    }

    private bool TryManeuver(EntityUid uid, Wh40kSpaceBossComponent component, TransformComponent xform,
        Vector2 threat, bool evade)
    {
        var origin = _transform.GetWorldPosition(xform);
        var direction = threat - origin;
        if (direction.LengthSquared() < 0.01f || xform.MapUid is not { } map)
            return false;
        direction = direction.Normalized();
        var side = new Vector2(-direction.Y, direction.X);
        var velocity = _physics.GetMapLinearVelocity(uid, xform: xform);
        if (Vector2.Dot(side, velocity) < 0)
            side = -side;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var position = origin + side * component.EvasionDistance + direction * (evade ? -10 : 20);
            if (IsClearFlightPath(uid, xform, position))
            {
                SetDestination(uid, component, map, position);
                component.LookPosition = position;
                component.ManeuverUntil = _timing.CurTime + TimeSpan.FromSeconds(evade ? 2 : 3);
                return true;
            }
            side = -side;
        }
        return false;
    }

    private bool IsClearFlightPath(EntityUid uid, TransformComponent xform, Vector2 position)
    {
        var origin = _transform.GetWorldPosition(xform);
        var delta = position - origin;
        var length = delta.Length();
        if (length < 0.01f)
            return true;
        if (_physics.IntersectRay(xform.MapID,
                new CollisionRay(origin, delta / length, (int) (CollisionGroup.Impassable | CollisionGroup.BulletImpassable)),
                length, uid, false).Any(hit => !HasComp<ShipShieldComponent>(hit.HitEntity) && !IsBossAlly(uid, hit.HitEntity)))
            return false;
        var steps = Math.Max(1, (int) MathF.Ceiling(length / FlightPathSampleDistance));
        for (var step = 1; step <= steps; step++)
        {
            var sample = Vector2.Lerp(origin, position, (float) step / steps);
            if (_safety.HasRule(xform.MapID, sample, KoronusSafetyRule.StationProtection | KoronusSafetyRule.HostileNpcAutoCleanup) ||
                _maps.TryFindGridAt(new MapCoordinates(sample, xform.MapID), out _, out _))
                return false;
        }
        return true;
    }

    private const float FlightPathSampleDistance = 16f;

    private void Roam(EntityUid uid, Wh40kSpaceBossComponent component, TransformComponent xform)
    {
        component.LookPosition = null;
        if (component.Destination is { } destination &&
            Vector2.DistanceSquared(_transform.ToMapCoordinates(destination).Position, _transform.GetWorldPosition(xform)) > 9)
            return;

        if (component.Home is not { } home || xform.MapUid is not { } map)
            return;
        var centre = _transform.ToMapCoordinates(home).Position;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var position = centre + _random.NextVector2(component.RoamRadius);
            if (Vector2.DistanceSquared(position, _transform.GetWorldPosition(xform)) < component.HeadDistance * component.HeadDistance ||
                !IsClearFlightPath(uid, xform, position))
                continue;
            SetDestination(uid, component, map, position);
            return;
        }
        _steering.Unregister(uid);
        component.Destination = null;
    }
}
