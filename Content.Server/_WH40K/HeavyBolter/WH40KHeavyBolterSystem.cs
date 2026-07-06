using System.Numerics;
using System.Linq;
using Content.Shared._WH40K.HeavyBolter;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Mobs;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.HeavyBolter;

public sealed partial class WH40KHeavyBolterSystem : EntitySystem
{
    private const float ArcDotEpsilon = 0.001f;
    private const string WallTag = "Wall";
    private const string WindowTag = "Window";
    private const string AirlockTag = "Airlock";
    private const string MagazineSlot = "gun_magazine";

    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly FixtureSystem _fixture = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KHeavyBolterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, HandheldEntityPlacementAttemptEvent>(OnPlacementAttempt);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, HandheldEntityPlacementCompleteEvent>(OnPlacementComplete);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, HandheldEntityFoldAttemptEvent>(OnFoldAttempt);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, HandheldEntityFoldCompleteEvent>(OnFoldComplete);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<BuckleComponent, MobStateChangedEvent>(OnOperatorMobStateChanged);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, WH40KHeavyBolterRotateLeftActionEvent>(OnRotateLeftAction);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, WH40KHeavyBolterRotateRightActionEvent>(OnRotateRightAction);
        SubscribeLocalEvent<InstantActionComponent, ActionPerformedEvent>(OnActionPerformed);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WH40KHeavyBolterComponent, StrapComponent>();
        while (query.MoveNext(out var uid, out var bolterComp, out var strapComp))
        {
            if (!bolterComp.Deployed || strapComp.BuckledEntities.Count == 0)
                continue;

            foreach (var buckledUid in strapComp.BuckledEntities)
            {
                if (!TryComp<BuckleComponent>(buckledUid, out var buckleComp) || buckleComp.BuckledTo != uid)
                    continue;

                SnapOperatorToRearOffset((uid, bolterComp), (buckledUid, buckleComp), strapComp.BuckleOffset, resetVelocity: true);
            }
        }
    }

    private void OnMapInit(Entity<WH40KHeavyBolterComponent> bolter, ref MapInitEvent args)
    {
        EnsureActionEntities(bolter);
        SyncRotateActionCooldowns(bolter);
        RefreshGunModifiers(bolter);
        NormalizeDeployedState(bolter);
        SyncMagazineSlotLock(bolter);
        SyncMagazineVisualState(bolter);
    }

    private void OnPlacementAttempt(Entity<WH40KHeavyBolterComponent> bolter, ref HandheldEntityPlacementAttemptEvent args)
    {
        NormalizeDeployedState(bolter);
        if (bolter.Comp.Deployed || TryGetCooldownRemainingSeconds(bolter, out _))
        {
            args.Cancel();
            return;
        }

        args.DeployDelay = bolter.Comp.DeployDelay;
    }

    private void OnPlacementComplete(Entity<WH40KHeavyBolterComponent> bolter, ref HandheldEntityPlacementCompleteEvent args)
    {
        DropHeldItems(args.User);

        var xform = Transform(bolter);
        _transform.SetCoordinates(bolter.Owner, xform, args.Coordinates, args.Direction.ToAngle());
        _transform.AnchorEntity(bolter.Owner, xform);

        bolter.Comp.Deployed = true;
        bolter.Comp.LastToggleAt = _timing.CurTime;
        Dirty(bolter);

        NormalizeDeployedState(bolter);
        if (bolter.Comp.DeploySound != null)
            _audio.PlayPvs(bolter.Comp.DeploySound, bolter);

        args.Handled = true;
    }

    private void OnFoldAttempt(Entity<WH40KHeavyBolterComponent> bolter, ref HandheldEntityFoldAttemptEvent args)
    {
        NormalizeDeployedState(bolter);
        if (!bolter.Comp.Deployed || TryGetCooldownRemainingSeconds(bolter, out _))
        {
            args.Cancel();
            return;
        }

        args.FoldDelay = bolter.Comp.FoldDelay;
        args.NeedHand = false;
        args.BreakOnHandChange = false;
    }

    private void OnFoldComplete(Entity<WH40KHeavyBolterComponent> bolter, ref HandheldEntityFoldCompleteEvent args)
    {
        if (TryComp<StrapComponent>(bolter, out var strap))
        {
            foreach (var buckledUid in strap.BuckledEntities.ToArray())
            {
                if (TryComp<BuckleComponent>(buckledUid, out var buckleComp) && buckleComp.BuckledTo == bolter.Owner)
                    _buckle.TryUnbuckle((buckledUid, buckleComp), args.User, popup: false);
            }
        }

        var xform = Transform(bolter);
        _transform.Unanchor(bolter.Owner, xform);

        bolter.Comp.Deployed = false;
        bolter.Comp.LastToggleAt = _timing.CurTime;
        Dirty(bolter);

        NormalizeDeployedState(bolter);
        if (bolter.Comp.FoldSound != null)
            _audio.PlayPvs(bolter.Comp.FoldSound, bolter);

        args.Handled = true;
    }

    private void OnGetVerbs(Entity<WH40KHeavyBolterComponent> bolter, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || !bolter.Comp.Deployed)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wh40k-heavy-bolter-verb-fold"),
            Act = () => RaiseLocalEvent(bolter.Owner, new HandheldEntityFoldRequestEvent(user)),
        });
    }

    private void OnAnchorStateChanged(Entity<WH40KHeavyBolterComponent> bolter, ref AnchorStateChangedEvent args)
    {
        NormalizeDeployedState(bolter);
    }

    private void OnAttemptShoot(Entity<WH40KHeavyBolterComponent> bolter, ref AttemptShootEvent args)
    {
        if (!IsOperatorControlAllowed(bolter, args.User))
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("wh40k-heavy-bolter-operator-required");
            return;
        }

        if (!TryComp<GunComponent>(bolter, out var gun) ||
            !TryGetShotDirection(bolter, gun, out var shotDirection) ||
            !TryGetForwardDirection(bolter, out var forwardDirection))
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("wh40k-heavy-bolter-invalid-shot-position");
            return;
        }

        var halfArc = Math.Clamp(bolter.Comp.FireArcDegrees, 0.1f, 360f) * 0.5f;
        if (halfArc >= 179.9f)
            return;

        var dot = Vector2.Dot(forwardDirection, shotDirection);
        var minDot = MathF.Cos((MathF.PI / 180f) * halfArc);
        if (dot >= minDot + ArcDotEpsilon)
            return;

        args.Cancelled = true;
        args.Message = Loc.GetString("wh40k-heavy-bolter-arc-limit");
    }

    private void OnStrapAttempt(Entity<WH40KHeavyBolterComponent> bolter, ref StrapAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (IsRearOperatorSpotOccupied(bolter.Owner, args.Buckle.Owner, args.Strap.Comp.BuckleOffset))
        {
            args.Cancelled = true;
            return;
        }

    }

    private void OnStrapped(Entity<WH40KHeavyBolterComponent> bolter, ref StrappedEvent args)
    {
        DropHeldItems(args.Buckle.Owner);
        GrantOperatorActions(bolter, args.Buckle.Owner);
        SnapOperatorToRearOffset(bolter, args.Buckle, args.Strap.Comp.BuckleOffset, resetVelocity: true);
    }

    private void OnUnstrapped(Entity<WH40KHeavyBolterComponent> bolter, ref UnstrappedEvent args)
    {
        _actions.RemoveProvidedActions(args.Buckle.Owner, bolter);
        MoveOperatorToRearExit(bolter, args.Buckle.Owner, args.Strap.Comp.BuckleOffset);
    }

    private void OnOperatorMobStateChanged(Entity<BuckleComponent> buckle, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive ||
            !buckle.Comp.Buckled ||
            buckle.Comp.BuckledTo is not { } strappedTo ||
            !HasComp<WH40KHeavyBolterComponent>(strappedTo))
        {
            return;
        }

        _buckle.TryUnbuckle(buckle.Owner, buckle.Owner, buckle.Comp, popup: false);
    }

    private void OnRotateLeftAction(Entity<WH40KHeavyBolterComponent> bolter, ref WH40KHeavyBolterRotateLeftActionEvent args)
    {
        if (!args.Handled)
            args.Handled = TryRotateBolter(bolter, args.Performer, -1f);
    }

    private void OnRotateRightAction(Entity<WH40KHeavyBolterComponent> bolter, ref WH40KHeavyBolterRotateRightActionEvent args)
    {
        if (!args.Handled)
            args.Handled = TryRotateBolter(bolter, args.Performer, 1f);
    }

    private void OnActionPerformed(Entity<InstantActionComponent> action, ref ActionPerformedEvent args)
    {
        if (!IsRotateAction(action.Owner) ||
            action.Comp.Container is not { } containerUid ||
            !TryComp<WH40KHeavyBolterComponent>(containerUid, out var bolterComp))
        {
            return;
        }

        SyncRotateActionCooldowns((containerUid, bolterComp), args.Performer);
    }

    private bool TryGetShotDirection(Entity<WH40KHeavyBolterComponent> bolter, GunComponent gun, out Vector2 shotDirection)
    {
        shotDirection = Vector2.Zero;
        if (gun.ShootCoordinates is not { } targetCoordinates)
            return false;

        var fromMap = GetShotOriginMapCoordinates(bolter);
        var toMap = _transform.ToMapCoordinates(targetCoordinates);
        if (fromMap.MapId == MapId.Nullspace || toMap.MapId == MapId.Nullspace || fromMap.MapId != toMap.MapId)
            return false;

        var vector = toMap.Position - fromMap.Position;
        if (vector.LengthSquared() <= 0.0001f)
            return false;

        shotDirection = vector.Normalized();
        return true;
    }

    private MapCoordinates GetShotOriginMapCoordinates(Entity<WH40KHeavyBolterComponent> bolter)
    {
        if (TryComp<BuckleMountedGunComponent>(bolter, out var mounted) &&
            mounted.ShootOriginOffset.LengthSquared() > 0.0001f)
        {
            return _transform.ToMapCoordinates(new EntityCoordinates(bolter, mounted.ShootOriginOffset));
        }

        return _transform.GetMapCoordinates(bolter);
    }

    private bool TryGetForwardDirection(Entity<WH40KHeavyBolterComponent> bolter, out Vector2 forwardDirection)
    {
        forwardDirection = Vector2.Zero;

        if (TryComp<StrapComponent>(bolter, out var strap))
        {
            var rearLocal = strap.BuckleOffset;
            if (rearLocal.LengthSquared() > 0.0001f)
            {
                var fromRearToFront = _transform.GetWorldRotation(bolter).RotateVec(-rearLocal);
                if (fromRearToFront.LengthSquared() > 0.0001f)
                {
                    forwardDirection = fromRearToFront.Normalized();
                    return true;
                }
            }
        }

        var fallback = _transform.GetWorldRotation(bolter).ToWorldVec();
        if (fallback.LengthSquared() <= 0.0001f)
            return false;

        forwardDirection = fallback.Normalized();
        return true;
    }

    private bool CanOperateBolter(Entity<WH40KHeavyBolterComponent> bolter)
    {
        return bolter.Comp.Deployed &&
               Transform(bolter).Anchored &&
               !_container.IsEntityInContainer(bolter);
    }

    private void NormalizeDeployedState(Entity<WH40KHeavyBolterComponent> bolter)
    {
        var canOperate = CanOperateBolter(bolter);
        _buckle.StrapSetEnabled(bolter, canOperate);
        _appearance.SetData(bolter, WH40KHeavyBolterVisuals.State, canOperate ? WH40KHeavyBolterVisualState.Deployed : WH40KHeavyBolterVisualState.Folded);

        if (_fixture.GetFixtureOrNull(bolter, bolter.Comp.FixtureId) is { } fixture)
            _physics.SetHard(bolter, fixture, canOperate);

        if (!canOperate && bolter.Comp.Deployed && !Transform(bolter).Anchored)
        {
            bolter.Comp.Deployed = false;
            Dirty(bolter);
        }

        SyncMagazineSlotLock(bolter);
        SyncMagazineVisualState(bolter);
    }

    private void SyncMagazineSlotLock(Entity<WH40KHeavyBolterComponent> bolter)
    {
        if (TryComp<ItemSlotsComponent>(bolter, out var slots))
            _itemSlots.SetLock(bolter, MagazineSlot, !CanOperateBolter(bolter), slots);
    }

    private void SyncMagazineVisualState(Entity<WH40KHeavyBolterComponent> bolter)
    {
        _appearance.SetData(bolter, AmmoVisuals.MagLoaded, CanOperateBolter(bolter) && HasMagazineLoaded(bolter.Owner));
    }

    private bool HasMagazineLoaded(EntityUid bolterUid)
    {
        return _itemSlots.GetItemOrNull(bolterUid, MagazineSlot) != null;
    }

    private bool TryGetCooldownRemainingSeconds(Entity<WH40KHeavyBolterComponent> bolter, out int remainingSeconds)
    {
        remainingSeconds = 0;
        var nextReadyAt = bolter.Comp.LastToggleAt + bolter.Comp.ToggleCooldown;
        if (nextReadyAt <= _timing.CurTime)
            return false;

        remainingSeconds = Math.Max(1, (int) Math.Ceiling((nextReadyAt - _timing.CurTime).TotalSeconds));
        return true;
    }

    private void SnapOperatorToRearOffset(Entity<WH40KHeavyBolterComponent> bolter, Entity<BuckleComponent> buckle, Vector2 rearLocalOffset, bool resetVelocity)
    {
        var buckleXform = Transform(buckle);
        _transform.SetCoordinates(buckle, buckleXform, new EntityCoordinates(bolter, rearLocalOffset), Angle.Zero);
        buckleXform.ActivelyLerping = false;

        if (!resetVelocity || !TryComp<PhysicsComponent>(buckle, out var physics))
            return;

        _physics.SetLinearVelocity(buckle, Vector2.Zero, body: physics);
        _physics.SetAngularVelocity(buckle, 0f, body: physics);
    }

    private void MoveOperatorToRearExit(Entity<WH40KHeavyBolterComponent> bolter, EntityUid operatorUid, Vector2 rearLocalOffset)
    {
        if (rearLocalOffset.LengthSquared() <= 0.0001f)
            return;

        var bolterMap = _transform.GetMapCoordinates(bolter);
        if (bolterMap.MapId == MapId.Nullspace)
            return;

        var rearWorld = _transform.GetWorldRotation(bolter).RotateVec(rearLocalOffset);
        if (rearWorld.LengthSquared() <= 0.0001f)
            return;

        var exitCoordinates = new MapCoordinates(
            bolterMap.Position + rearWorld.Normalized() * MathF.Max(rearLocalOffset.Length() + 0.35f, 0.9f),
            bolterMap.MapId);

        _transform.SetMapCoordinates(operatorUid, exitCoordinates);
        Transform(operatorUid).ActivelyLerping = false;

        if (TryComp<PhysicsComponent>(operatorUid, out var physics))
        {
            _physics.SetLinearVelocity(operatorUid, Vector2.Zero, body: physics);
            _physics.SetAngularVelocity(operatorUid, 0f, body: physics);
        }
    }

    private bool IsOperatorControlAllowed(Entity<WH40KHeavyBolterComponent> bolter, EntityUid user)
    {
        return CanOperateBolter(bolter) &&
               (!bolter.Comp.RequireBuckledOperator ||
                (TryComp<BuckleComponent>(user, out var buckle) && buckle.BuckledTo == bolter.Owner));
    }

    private bool TryRotateBolter(Entity<WH40KHeavyBolterComponent> bolter, EntityUid performer, float directionSign)
    {
        if (!IsOperatorControlAllowed(bolter, performer))
            return false;

        if (bolter.Comp.LastRotateAt + bolter.Comp.RotateCooldown > _timing.CurTime)
        {
            SyncRotateActionCooldowns(bolter, performer);
            return false;
        }

        if (TryComp<StrapComponent>(bolter, out var strapComp))
        {
            var previewRotation = _transform.GetWorldRotation(bolter) + Angle.FromDegrees(MathF.Abs(bolter.Comp.RotateStepDegrees) * directionSign);
            foreach (var buckledUid in strapComp.BuckledEntities)
            {
                if (TryComp<BuckleComponent>(buckledUid, out var buckleComp) &&
                    buckleComp.BuckledTo == bolter.Owner &&
                    IsRearOperatorSpotOccupied(bolter.Owner, buckledUid, strapComp.BuckleOffset, previewRotation))
                {
                    return false;
                }
            }
        }

        var xform = Transform(bolter);
        _transform.SetLocalRotation(bolter, xform.LocalRotation + Angle.FromDegrees(MathF.Abs(bolter.Comp.RotateStepDegrees) * directionSign), xform);

        bolter.Comp.LastRotateAt = _timing.CurTime;
        Dirty(bolter);
        SyncRotateActionCooldowns(bolter, performer);

        if (TryComp<StrapComponent>(bolter, out var currentStrap))
            SnapBuckledOperatorsToRear(bolter, currentStrap);

        return true;
    }

    private void SnapBuckledOperatorsToRear(Entity<WH40KHeavyBolterComponent> bolter, StrapComponent strapComp)
    {
        foreach (var buckledUid in strapComp.BuckledEntities)
        {
            if (!TryComp<BuckleComponent>(buckledUid, out var buckleComp) || buckleComp.BuckledTo != bolter.Owner)
                continue;

            SnapOperatorToRearOffset((bolter.Owner, bolter.Comp), (buckledUid, buckleComp), strapComp.BuckleOffset, resetVelocity: true);
        }
    }

    private void GrantOperatorActions(Entity<WH40KHeavyBolterComponent> bolter, EntityUid user)
    {
        EnsureActionEntities(bolter);
        _actions.AddAction(user, ref bolter.Comp.RotateLeftActionEntity, bolter.Comp.RotateLeftAction, bolter);
        _actions.AddAction(user, ref bolter.Comp.RotateRightActionEntity, bolter.Comp.RotateRightAction, bolter);
        SyncRotateActionCooldowns(bolter, user);
    }

    private void EnsureActionEntities(Entity<WH40KHeavyBolterComponent> bolter)
    {
        _actionContainer.EnsureAction(bolter, ref bolter.Comp.RotateLeftActionEntity, bolter.Comp.RotateLeftAction);
        _actionContainer.EnsureAction(bolter, ref bolter.Comp.RotateRightActionEntity, bolter.Comp.RotateRightAction);
    }

    private void SyncRotateActionCooldowns(Entity<WH40KHeavyBolterComponent> bolter, EntityUid? user = null)
    {
        var start = bolter.Comp.LastRotateAt;
        var end = start + bolter.Comp.RotateCooldown;

        if (end <= _timing.CurTime)
        {
            _actions.ClearCooldown(bolter.Comp.RotateLeftActionEntity);
            _actions.ClearCooldown(bolter.Comp.RotateRightActionEntity);
        }
        else
        {
            _actions.SetCooldown(bolter.Comp.RotateLeftActionEntity, start, end);
            _actions.SetCooldown(bolter.Comp.RotateRightActionEntity, start, end);
        }

        if (user is { } preferredUser)
            SyncRotateActionCooldownsForUser(bolter.Owner, preferredUser, start, end);
    }

    private void SyncRotateActionCooldownsForUser(EntityUid bolterUid, EntityUid user, TimeSpan start, TimeSpan end)
    {
        foreach (var action in _actions.GetActions(user))
        {
            if (action.Comp.Container != bolterUid || !IsRotateAction(action.Id))
                continue;

            if (end <= _timing.CurTime)
                _actions.ClearCooldown(action.Id);
            else
                _actions.SetCooldown(action.Id, start, end);
        }
    }

    private bool IsRotateAction(EntityUid actionUid)
    {
        return TryComp<InstantActionComponent>(actionUid, out var instant) &&
               instant.Event is WH40KHeavyBolterRotateLeftActionEvent or WH40KHeavyBolterRotateRightActionEvent;
    }

    private void RefreshGunModifiers(Entity<WH40KHeavyBolterComponent> bolter)
    {
        if (TryComp<GunComponent>(bolter, out var gunComp))
            _gun.RefreshModifiers((bolter.Owner, gunComp));
    }

    private void DropHeldItems(EntityUid user)
    {
        if (!TryComp<HandsComponent>(user, out var hands))
            return;

        foreach (var hand in _hands.EnumerateHands(user, hands))
        {
            if (hand.HeldEntity != null)
                _hands.TryDrop(user, hand, checkActionBlocker: false, handsComp: hands);
        }
    }

    private bool IsRearOperatorSpotOccupied(EntityUid bolterUid, EntityUid operatorUid, Vector2 rearLocalOffset, Angle? worldRotationOverride = null)
    {
        var bolterMapCoordinates = _transform.GetMapCoordinates(bolterUid);
        if (bolterMapCoordinates.MapId == MapId.Nullspace)
            return true;

        var rotation = worldRotationOverride ?? _transform.GetWorldRotation(bolterUid);
        var operatorMapPosition = bolterMapCoordinates.Position + rotation.RotateVec(rearLocalOffset);
        var operatorBounds = _lookup.GetAABBNoContainer(operatorUid, operatorMapPosition, Angle.Zero);
        var intersecting = _lookup.GetEntitiesIntersecting(
            bolterMapCoordinates.MapId,
            operatorBounds,
            LookupFlags.Dynamic | LookupFlags.Static);

        foreach (var entity in intersecting)
        {
            if (entity == bolterUid || entity == operatorUid)
                continue;

            if (IsRearRotationObstacle(entity))
                return true;
        }

        return false;
    }

    private bool IsRearRotationObstacle(EntityUid entity)
    {
        return _tag.HasTag(entity, WallTag) ||
               _tag.HasTag(entity, WindowTag) ||
               _tag.HasTag(entity, AirlockTag);
    }
}
