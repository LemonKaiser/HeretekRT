using System.Numerics;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Shared._WH40K.HeavyBolter;

/// <summary>
/// Blocks unrelated interactions while a user is operating a deployed heavy bolter
/// and keeps buckle prediction clean by validating the rear operator slot up front.
/// </summary>
public sealed partial class SharedWH40KHeavyBolterOperatorSystem : EntitySystem
{
    private const string WallTag = "Wall";
    private const string WindowTag = "Window";
    private const string AirlockTag = "Airlock";

    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KHeavyBolterComponent, InteractHandEvent>(OnBolterInteractHand, before: [typeof(SharedBuckleSystem)]);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, StrapAttemptEvent>(OnStrapAttempt);

        SubscribeLocalEvent<BuckleComponent, UseAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<BuckleComponent, PickupAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<BuckleComponent, DropAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<BuckleComponent, ThrowAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<BuckleComponent, IsEquippingAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<BuckleComponent, IsUnequippingAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<BuckleComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<BuckleComponent, CanAttackFromContainerEvent>(OnCanAttackFromContainer);
        SubscribeLocalEvent<BuckleComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<BuckleComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<BuckleComponent, GetMeleeWeaponEvent>(OnGetMeleeWeapon);
    }

    private void OnBolterInteractHand(Entity<WH40KHeavyBolterComponent> ent, ref InteractHandEvent args)
    {
        if (TryComp<BuckleComponent>(args.User, out var buckle) && buckle.BuckledTo == ent.Owner)
        {
            _buckle.TryUnbuckle((args.User, buckle), args.User, popup: true);
            args.Handled = true;
        }
    }

    private void OnCancellableAttempt(EntityUid uid, BuckleComponent component, CancellableEntityEventArgs args)
    {
        if (IsOperatingHeavyBolter(component, out _))
            args.Cancel();
    }

    private void OnInteractionAttempt(EntityUid uid, BuckleComponent component, ref InteractionAttemptEvent args)
    {
        if (!IsOperatingHeavyBolter(component, out var bolterUid))
            return;

        if (args.Target == bolterUid)
            return;

        args.Cancelled = true;
    }

    private void OnCanAttackFromContainer(EntityUid uid, BuckleComponent component, ref CanAttackFromContainerEvent args)
    {
        if (IsOperatingHeavyBolter(component, out _))
            args.CanAttack = false;
    }

    private void OnShotAttempted(EntityUid uid, BuckleComponent component, ref ShotAttemptedEvent args)
    {
        if (!IsOperatingHeavyBolter(component, out var bolterUid))
            return;

        if (args.Used.Owner != bolterUid)
            args.Cancel();
    }

    private void OnAttackAttempt(EntityUid uid, BuckleComponent component, AttackAttemptEvent args)
    {
        if (!IsOperatingHeavyBolter(component, out var bolterUid))
            return;

        var allowMountedGunPath =
            args.Weapon == null &&
            !args.Disarm &&
            args.Target == null &&
            HasComp<GunComponent>(bolterUid);

        if (!allowMountedGunPath)
            args.Cancel();
    }

    private void OnGetMeleeWeapon(EntityUid uid, BuckleComponent component, GetMeleeWeaponEvent args)
    {
        if (!IsOperatingHeavyBolter(component, out _))
            return;

        args.Weapon = null;
        args.Handled = true;
    }

    private void OnStrapAttempt(Entity<WH40KHeavyBolterComponent> ent, ref StrapAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (IsOperatorSpotOccupied(ent.Owner, args.Buckle.Owner, args.Strap.Comp.BuckleOffset))
            args.Cancelled = true;
    }

    private bool IsOperatingHeavyBolter(BuckleComponent buckle, out EntityUid bolterUid)
    {
        bolterUid = default;

        if (!buckle.Buckled || buckle.BuckledTo is not { } strappedTo)
            return false;

        if (!TryComp<WH40KHeavyBolterComponent>(strappedTo, out var bolterComp) ||
            !bolterComp.Deployed ||
            !TryComp<StrapComponent>(strappedTo, out var strapComp) ||
            !strapComp.Enabled ||
            !Transform(strappedTo).Anchored)
        {
            return false;
        }

        bolterUid = strappedTo;
        return true;
    }

    private bool IsOperatorSpotOccupied(EntityUid bolterUid, EntityUid operatorUid, Vector2 rearLocalOffset)
    {
        var operatorCoordinates = new EntityCoordinates(bolterUid, rearLocalOffset);
        if (!operatorCoordinates.IsValid(EntityManager))
            return true;

        var operatorMapCoordinates = _transform.ToMapCoordinates(operatorCoordinates);
        if (operatorMapCoordinates.MapId == MapId.Nullspace)
            return true;

        var operatorBounds = _lookup.GetAABBNoContainer(operatorUid, operatorMapCoordinates.Position, Angle.Zero);
        var intersecting = _lookup.GetEntitiesIntersecting(
            operatorMapCoordinates.MapId,
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
