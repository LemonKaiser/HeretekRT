using System.Numerics;
using Content.Shared._Mono.Weapons.Hitscan.Components;
using Content.Shared.Actions;
using Content.Shared.ActionBlocker;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.Combat.PhantomStep;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Standing;
using Content.Shared.Toggleable;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Hitscan.Systems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Combat.PhantomStep;

public sealed partial class WH40KPhantomStepSystem : EntitySystem
{
    public const int MaximumCharacterCharges = Wh40kCharacteristicEffects.MaximumPhantomStepCharges;

    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KPhantomStepComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WH40KPhantomStepComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KPhantomStepComponent, ToggleActionEvent>(OnToggleAction);
        SubscribeLocalEvent<WH40KPhantomStepComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<WH40KPhantomStepComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<HitscanBasicRaycastComponent, HitscanRaycastFiredEvent>(OnBasicHitscanFired,
            before: [typeof(HitscanReflectSystem), typeof(HitscanBasicDamageSystem), typeof(HitscanStunSystem)]);
        SubscribeLocalEvent<HitscanMultiRaycastComponent, HitscanRaycastFiredEvent>(OnMultiHitscanFired,
            before: [typeof(HitscanReflectSystem), typeof(HitscanBasicDamageSystem), typeof(HitscanStunSystem)]);
        SubscribeLocalEvent<WH40KPhantomStepComponent, ProjectileImpactAttemptEvent>(OnProjectileImpact);
        SubscribeLocalEvent<WH40KPhantomStepComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KPhantomStepComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var step, out var xform))
        {
            if (step.Dashing)
                UpdateDash(uid, step, xform, now);

            if (step.DodgedProjectile is not null && now > step.DodgedProjectileUntil)
                step.DodgedProjectile = null;

            if (step.Charges >= step.MaxCharges || step.NextRecharge == TimeSpan.Zero || now < step.NextRecharge)
                continue;

            var cooldown = step.Cooldown > TimeSpan.Zero ? step.Cooldown : TimeSpan.FromMilliseconds(1);
            while (step.Charges < step.MaxCharges && now >= step.NextRecharge)
            {
                step.Charges++;
                step.NextRecharge += cooldown;
            }

            if (step.Charges >= step.MaxCharges)
                step.NextRecharge = TimeSpan.Zero;

            Dirty(uid, step);
            SyncAction(step, uid);
        }
    }

    /// <summary>
    /// Configures Phantom Step for a character build. The caller owns the eligibility check;
    /// this method keeps action state and rechargeable charges in sync after the component is
    /// added to an already spawned mob.
    /// </summary>
    public void ConfigureForCharacter(EntityUid uid, int maxCharges)
    {
        var existed = TryComp<WH40KPhantomStepComponent>(uid, out var existing);
        var previousMaxCharges = existed ? existing!.MaxCharges : 0;
        var previousCharges = existed ? existing!.Charges : 0;
        var step = EnsureComp<WH40KPhantomStepComponent>(uid);
        var charges = Math.Clamp(maxCharges, 0, MaximumCharacterCharges);

        if (!existed || step.MaxCharges != charges)
        {
            step.MaxCharges = charges;
            step.Charges = existed
                ? GetChargesAfterReconfigure(previousMaxCharges, previousCharges, charges)
                : charges;
        }

        if (charges == 0)
        {
            if (step.Dashing)
                StopDash(uid, step, Transform(uid), snapToEnd: false);
            if (step.ToggleActionEntity is { } action)
                _actions.RemoveAction(uid, action);
            step.ToggleActionEntity = null;
            step.PendingMeleeSource = null;
            step.DodgedProjectile = null;
            step.NextRecharge = TimeSpan.Zero;
        }
        else
        {
            if (step.ToggleActionEntity is not { } action || !Exists(action))
                _actions.AddAction(uid, ref step.ToggleActionEntity, step.ToggleAction, uid);

            if (step.Charges >= charges)
                step.NextRecharge = TimeSpan.Zero;
            else if (step.NextRecharge == TimeSpan.Zero)
                step.NextRecharge = _timing.CurTime + step.Cooldown;
        }

        Dirty(uid, step);
        SyncAction(step, uid);
    }

    internal static int GetChargesAfterReconfigure(
        int previousMaxCharges,
        int previousCharges,
        int requestedMaxCharges)
    {
        var charges = Math.Clamp(requestedMaxCharges, 0, MaximumCharacterCharges);
        var addedCapacity = Math.Max(0, charges - Math.Max(0, previousMaxCharges));
        return Math.Clamp(previousCharges + addedCapacity, 0, charges);
    }

    private void OnStartup(Entity<WH40KPhantomStepComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.MaxCharges > 0)
            _actions.AddAction(ent.Owner, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction, ent.Owner);
        SyncAction(ent.Comp, ent.Owner);
    }

    private void OnShutdown(Entity<WH40KPhantomStepComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ToggleActionEntity != null)
            _actions.RemoveAction(ent.Owner, ent.Comp.ToggleActionEntity);

        ent.Comp.ToggleActionEntity = null;
    }

    private void OnToggleAction(Entity<WH40KPhantomStepComponent> ent, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        ent.Comp.Enabled = !ent.Comp.Enabled;
        if (!ent.Comp.Enabled)
            ent.Comp.PendingMeleeSource = null;
        Dirty(ent.Owner, ent.Comp);
        SyncAction(ent.Comp, ent.Owner);
        args.Handled = true;
    }

    private void OnAttacked(Entity<WH40KPhantomStepComponent> ent, ref AttackedEvent args)
    {
        if (TryTriggerDodge(ent, args.User, args.Used, PhantomStepThreatType.Melee))
        {
            ent.Comp.PendingMeleeSource = args.User;
            ent.Comp.PendingMeleeAt = _timing.CurTime;
        }
    }

    private void OnMobStateChanged(Entity<WH40KPhantomStepComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        ent.Comp.PendingMeleeSource = null;
        ent.Comp.DodgedProjectile = null;
        if (ent.Comp.Dashing)
            StopDash(ent.Owner, ent.Comp, Transform(ent.Owner), snapToEnd: false);
        else
            Dirty(ent.Owner, ent.Comp);
    }

    private void OnBasicHitscanFired(Entity<HitscanBasicRaycastComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        TryDodgeHitscan(ref args);
    }

    private void OnMultiHitscanFired(Entity<HitscanMultiRaycastComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        TryDodgeHitscan(ref args);
    }

    private void TryDodgeHitscan(ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled || args.HitEntities.Count == 0)
            return;

        List<EntityUid>? dodged = null;
        foreach (var target in args.HitEntities)
        {
            if (!TryComp<WH40KPhantomStepComponent>(target, out var step))
                continue;

            if (TryTriggerDodge((target, step), args.Shooter ?? args.Gun, args.Gun, PhantomStepThreatType.Ranged))
            {
                dodged ??= [];
                dodged.Add(target);
            }
        }

        if (dodged != null)
        {
            foreach (var target in dodged)
                args.HitEntities.Remove(target);
        }
    }

    private void OnProjectileImpact(Entity<WH40KPhantomStepComponent> ent, ref ProjectileImpactAttemptEvent args)
    {
        if (args.Cancelled || IsSelfThreat(ent.Owner, args.Component.Shooter, args.Component.Weapon))
            return;

        if (ent.Comp.DodgedProjectile == args.ProjectileUid && _timing.CurTime <= ent.Comp.DodgedProjectileUntil)
        {
            args.Cancelled = true;
            return;
        }

        if (!TryTriggerDodge(ent,
                args.Component.Shooter ?? args.Component.Weapon ?? args.ProjectileUid,
                args.Component.Weapon,
                PhantomStepThreatType.Ranged))
            return;

        ent.Comp.DodgedProjectile = args.ProjectileUid;
        ent.Comp.DodgedProjectileUntil = _timing.CurTime +
            (ent.Comp.DashDuration > TimeSpan.FromMilliseconds(250)
                ? ent.Comp.DashDuration
                : TimeSpan.FromMilliseconds(250));
        args.Cancelled = true;
    }

    private void OnBeforeDamage(Entity<WH40KPhantomStepComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (ent.Comp.PendingMeleeSource is not { } source ||
            ent.Comp.PendingMeleeAt != _timing.CurTime ||
            args.Origin != source)
            return;

        ent.Comp.PendingMeleeSource = null;
        args.Cancelled = true;
    }

    private bool TryTriggerDodge(
        Entity<WH40KPhantomStepComponent> ent,
        EntityUid? source,
        EntityUid? weapon,
        PhantomStepThreatType threatType)
    {
        var now = _timing.CurTime;
        if (!ent.Comp.Enabled || ent.Comp.Dashing ||
            ent.Comp.MaxCharges <= 0 || ent.Comp.Charges <= 0 ||
            !CanDodgeThreat(ent.Comp, threatType) ||
            IsSelfThreat(ent.Owner, source, weapon) ||
            !CanDash(ent.Owner))
            return false;

        var xform = Transform(ent.Owner);
        if (!TryFindDodgeCoordinates(ent.Owner, source, ent.Comp, out var targetCoords))
            return false;

        var originCoords = xform.Coordinates;
        var originMap = _transform.GetMapCoordinates((ent.Owner, xform));
        var targetMap = _transform.ToMapCoordinates(targetCoords);
        if (originMap.MapId != targetMap.MapId || targetMap.MapId == MapId.Nullspace)
            return false;

        ent.Comp.Charges--;
        if (ent.Comp.Charges < ent.Comp.MaxCharges &&
            ent.Comp.NextRecharge == TimeSpan.Zero)
        {
            ent.Comp.NextRecharge = now + ent.Comp.Cooldown;
        }
        var dashDuration = ent.Comp.DashDuration > TimeSpan.Zero
            ? ent.Comp.DashDuration
            : TimeSpan.FromMilliseconds(1);
        ent.Comp.Dashing = true;
        ent.Comp.DashStartedAt = now;
        ent.Comp.DashEndsAt = now + dashDuration;
        ent.Comp.DashStart = originMap;
        ent.Comp.DashEnd = targetMap;
        ent.Comp.DashEndCoordinates = targetCoords;

        RaiseNetworkEvent(
            new WH40KPhantomStepTrailEvent(
                GetNetEntity(ent.Owner),
                GetNetCoordinates(originCoords),
                GetNetCoordinates(targetCoords),
                (float) dashDuration.TotalSeconds,
                (float) ent.Comp.TrailLifetime.TotalSeconds,
                ent.Comp.TrailCopies),
            Filter.Pvs(ent.Owner));

        Dirty(ent.Owner, ent.Comp);
        SyncAction(ent.Comp, ent.Owner);
        return true;
    }

    private void UpdateDash(
        EntityUid uid,
        WH40KPhantomStepComponent step,
        TransformComponent xform,
        TimeSpan now)
    {
        if (!step.Dashing)
            return;

        if (!CanDash(uid) ||
            step.DashStart == MapCoordinates.Nullspace ||
            step.DashEnd == MapCoordinates.Nullspace ||
            step.DashEndCoordinates == EntityCoordinates.Invalid ||
            _transform.GetMapCoordinates((uid, xform)).MapId != step.DashStart.MapId)
        {
            StopDash(uid, step, xform, snapToEnd: false);
            return;
        }

        var totalSeconds = Math.Max(0.001f, (float) (step.DashEndsAt - step.DashStartedAt).TotalSeconds);
        var elapsedSeconds = (float) (now - step.DashStartedAt).TotalSeconds;
        var progress = Math.Clamp(elapsedSeconds / totalSeconds, 0f, 1f);
        var nextPos = Vector2.Lerp(step.DashStart.Position, step.DashEnd.Position, progress);
        var currentMap = _transform.GetMapCoordinates((uid, xform));
        var nextMap = new MapCoordinates(nextPos, step.DashEnd.MapId);
        if (!IsDashPathSafe(uid, currentMap, nextMap))
        {
            StopDash(uid, step, xform, snapToEnd: false);
            return;
        }

        _transform.SetMapCoordinates((uid, xform), nextMap);

        if (progress < 1f)
            return;

        StopDash(uid, step, xform, snapToEnd: true);
    }

    private void StopDash(
        EntityUid uid,
        WH40KPhantomStepComponent step,
        TransformComponent xform,
        bool snapToEnd)
    {
        if (snapToEnd && step.DashEndCoordinates != EntityCoordinates.Invalid &&
            Exists(step.DashEndCoordinates.EntityId))
        {
            _transform.SetCoordinates(uid, xform, step.DashEndCoordinates);
            _transform.AttachToGridOrMap(uid, xform);
        }

        step.Dashing = false;
        step.DashStartedAt = TimeSpan.Zero;
        step.DashEndsAt = TimeSpan.Zero;
        step.DashStart = MapCoordinates.Nullspace;
        step.DashEnd = MapCoordinates.Nullspace;
        step.DashEndCoordinates = EntityCoordinates.Invalid;
        Dirty(uid, step);
    }

    private void SyncAction(WH40KPhantomStepComponent step, EntityUid owner)
    {
        if (step.ToggleActionEntity is not { } actionUid || !Exists(actionUid))
            return;

        _actions.SetToggled(actionUid, step.Enabled);

        var action = EnsureComp<WH40KPhantomStepActionComponent>(actionUid);
        var changed = false;

        if (action.Charges != step.Charges)
        {
            action.Charges = step.Charges;
            changed = true;
        }

        if (action.MaxCharges != step.MaxCharges)
        {
            action.MaxCharges = step.MaxCharges;
            changed = true;
        }

        if (action.RechargeDuration != step.Cooldown)
        {
            action.RechargeDuration = step.Cooldown;
            changed = true;
        }

        if (action.NextRecharge != step.NextRecharge)
        {
            action.NextRecharge = step.NextRecharge;
            changed = true;
        }

        if (changed)
            Dirty(actionUid, action);
    }

    private static bool CanDodgeThreat(WH40KPhantomStepComponent step, PhantomStepThreatType threatType)
    {
        return threatType switch
        {
            PhantomStepThreatType.Ranged => step.DodgeRanged,
            PhantomStepThreatType.Melee => step.DodgeMelee,
            _ => false,
        };
    }

    private bool CanDash(EntityUid uid)
    {
        return (!TryComp<MobStateComponent>(uid, out var mobState) ||
                mobState.CurrentState == MobState.Alive) &&
               _actionBlocker.CanMove(uid) &&
               !_standing.IsDown(uid);
    }

    private bool IsSelfThreat(EntityUid target, EntityUid? source, EntityUid? weapon)
    {
        return BelongsTo(target, source) || BelongsTo(target, weapon);
    }

    private bool BelongsTo(EntityUid target, EntityUid? entity)
    {
        for (var depth = 0; depth < 12 && entity is { } uid && Exists(uid); depth++)
        {
            if (uid == target)
                return true;

            entity = TryComp<TransformComponent>(uid, out var xform) && xform.ParentUid.IsValid()
                ? xform.ParentUid
                : null;
        }

        return false;
    }

    private bool TryFindDodgeCoordinates(
        EntityUid target,
        EntityUid? source,
        WH40KPhantomStepComponent step,
        out EntityCoordinates coordinates)
    {
        var origin = _transform.GetMapCoordinates(target);
        var directions = BuildCandidateDirections(origin, source);

        for (var distance = step.MaxDistance; distance >= step.MinDistance; distance--)
        {
            foreach (var direction in directions)
            {
                if (direction.LengthSquared() <= 0.001f)
                    continue;

                var candidate = new MapCoordinates(origin.Position + Vector2.Normalize(direction) * distance, origin.MapId);
                if (TryGetSafeCoordinates(candidate, out coordinates) &&
                    IsDashPathSafe(target, origin, _transform.ToMapCoordinates(coordinates)))
                {
                    return true;
                }
            }
        }

        coordinates = Transform(target).Coordinates;
        return false;
    }

    private List<Vector2> BuildCandidateDirections(MapCoordinates origin, EntityUid? source)
    {
        var away = Vector2.Zero;
        if (source is { } sourceUid && Exists(sourceUid))
        {
            var sourceMap = _transform.GetMapCoordinates(sourceUid);
            if (sourceMap.MapId == origin.MapId)
                away = origin.Position - sourceMap.Position;
        }

        if (away.LengthSquared() <= 0.001f)
            away = _random.NextAngle().ToVec();

        away = Vector2.Normalize(away);
        return new List<Vector2>
        {
            away,
            Rotate(away, MathF.PI / 4f),
            Rotate(away, -MathF.PI / 4f),
            Rotate(away, MathF.PI / 2f),
            Rotate(away, -MathF.PI / 2f),
            -away,
            _random.NextAngle().ToVec(),
            _random.NextAngle().ToVec(),
        };
    }

    private bool IsDashPathSafe(EntityUid mover, MapCoordinates start, MapCoordinates end)
    {
        if (start.MapId == MapId.Nullspace ||
            end.MapId == MapId.Nullspace ||
            start.MapId != end.MapId)
        {
            return false;
        }

        var delta = end.Position - start.Position;
        var distance = delta.Length();
        if (distance <= 0.01f)
            return true;

        var direction = delta / distance;
        var lateral = new Vector2(-direction.Y, direction.X) * 0.22f;
        for (var lane = -1; lane <= 1; lane++)
        {
            var ray = new CollisionRay(start.Position + lateral * lane, direction, (int) CollisionGroup.MobMask);
            foreach (var hit in _physics.IntersectRay(start.MapId, ray, distance, mover, false))
            {
                if (hit.HitEntity != mover && hit.Distance < distance - 0.02f)
                    return false;
            }
        }

        var steps = Math.Max(1, (int) MathF.Ceiling(distance / 0.35f));
        for (var i = 1; i <= steps; i++)
        {
            var progress = i / (float) steps;
            var sample = new MapCoordinates(Vector2.Lerp(start.Position, end.Position, progress), start.MapId);
            if (!IsSafeMapCoordinate(sample))
                return false;
        }

        return true;
    }

    private bool TryGetSafeCoordinates(MapCoordinates candidate, out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;

        if (!IsSafeMapCoordinate(candidate))
            return false;

        if (candidate.MapId == MapId.Nullspace)
            return false;

        if (!_mapManager.TryFindGridAt(candidate, out var gridUid, out var grid))
            return false;

        var tileIndices = _map.WorldToTile(gridUid, grid, candidate.Position);
        if (!_map.TryGetTileRef(gridUid, grid, tileIndices, out var tileRef))
            return false;

        if (tileRef.Tile.IsEmpty || _turf.IsSpace(tileRef))
            return false;

        if (_turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
            return false;

        coordinates = _turf.GetTileCenter(tileRef);
        return true;
    }

    private bool IsSafeMapCoordinate(MapCoordinates candidate)
    {
        if (candidate.MapId == MapId.Nullspace)
            return false;

        if (!_mapManager.TryFindGridAt(candidate, out var gridUid, out var grid))
            return false;

        var tileIndices = _map.WorldToTile(gridUid, grid, candidate.Position);
        if (!_map.TryGetTileRef(gridUid, grid, tileIndices, out var tileRef))
            return false;

        if (tileRef.Tile.IsEmpty || _turf.IsSpace(tileRef))
            return false;

        return !_turf.IsTileBlocked(tileRef, CollisionGroup.MobMask);
    }

    private static Vector2 Rotate(Vector2 vector, float radians)
    {
        var sin = MathF.Sin(radians);
        var cos = MathF.Cos(radians);
        return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
    }

    private enum PhantomStepThreatType : byte
    {
        Ranged,
        Melee,
    }
}
