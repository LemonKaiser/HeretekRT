using Content.Shared.Hands.EntitySystems;
using System.Linq;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared._WH40K.ClassProgression;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.ClassProgression;

/// <summary>
/// Applies the fixed, non-stacking movement and moving-shot costs of the WH40K weapon categories. Class skills
/// can only compensate these two independently named penalties through the public query methods below.
/// </summary>
public sealed partial class Wh40kClassWeaponHandlingSystem : EntitySystem
{
    public static readonly TimeSpan ShotPenaltyDuration = TimeSpan.FromSeconds(0.4);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kWeaponHandlingComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<Wh40kWeaponHandlingComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
        SubscribeLocalEvent<MovementSpeedModifierComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<Wh40kClassGunShotMovementComponent>();
        while (query.MoveNext(out var user, out var penalty))
        {
            if (penalty.ExpiresAt > _timing.CurTime)
                continue;

            RemComp<Wh40kClassGunShotMovementComponent>(user);
            _movement.RefreshMovementSpeedModifiers(user);
        }
    }

    public bool TryGetHeldHandling(
        EntityUid user,
        out EntityUid weapon,
        out Wh40kWeaponHandlingComponent handling)
    {
        weapon = default;
        handling = default!;
        foreach (var item in _hands.EnumerateHeld(user))
        {
            if (!TryComp<Wh40kWeaponHandlingComponent>(item, out var candidate) ||
                TryComp<WieldableComponent>(item, out var wieldable) && !wieldable.Wielded)
            {
                continue;
            }

            weapon = item;
            handling = candidate;
            return true;
        }

        return false;
    }

    public bool TryGetShotPenalty(EntityUid user, out EntityUid weapon, out WeaponHandlingValues values)
    {
        weapon = default;
        values = default;
        if (!TryComp<Wh40kClassGunShotMovementComponent>(user, out var penalty) ||
            penalty.ExpiresAt <= _timing.CurTime || !Exists(penalty.Weapon))
        {
            return false;
        }

        weapon = penalty.Weapon;
        values = GetValues(penalty.Category);
        return values.ShotPenaltyPercent > 0;
    }

    public bool IsMoving(EntityUid user)
    {
        return TryComp<InputMoverComponent>(user, out var mover) && mover.WishDir.LengthSquared() > 0.0001f;
    }

    public static WeaponHandlingValues GetValues(Wh40kWeaponHandlingCategory category)
    {
        return category switch
        {
            Wh40kWeaponHandlingCategory.Pistol => new WeaponHandlingValues(0, 0, 0),
            Wh40kWeaponHandlingCategory.SemiAutomaticRifle => new WeaponHandlingValues(2, 4, 10),
            Wh40kWeaponHandlingCategory.Automatic => new WeaponHandlingValues(5, 10, 15),
            Wh40kWeaponHandlingCategory.LightMachineGun => new WeaponHandlingValues(8, 16, 20),
            Wh40kWeaponHandlingCategory.HeavyMachineGun => new WeaponHandlingValues(10, 20, 25),
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
    }

    private void OnGunShot(Entity<Wh40kWeaponHandlingComponent> ent, ref GunShotEvent args)
    {
        var values = GetValues(ent.Comp.Category);
        if (values.ShotPenaltyPercent <= 0 || !Exists(args.User))
            return;

        var duration = ShotPenaltyDuration;
        if (TryComp<Wh40kClassRuntimeProfileComponent>(args.User, out var profile))
        {
            duration = profile.ActiveEffects.Values
                .Where(effect => effect.Mechanic == Wh40kClassRuntimeMechanic.GunShotPenaltyDurationOverride &&
                                 (!effect.RequiresEquipment || effect.SupportingItem == ent.Owner))
                .Select(effect => effect.Duration)
                .Where(value => value > TimeSpan.Zero)
                .DefaultIfEmpty(duration)
                .Min();
        }

        var penalty = EnsureComp<Wh40kClassGunShotMovementComponent>(args.User);
        penalty.Weapon = ent.Owner;
        penalty.Category = ent.Comp.Category;
        penalty.ExpiresAt = _timing.CurTime + duration;
        _movement.RefreshMovementSpeedModifiers(args.User);
    }

    private void OnGunRefreshModifiers(Entity<Wh40kWeaponHandlingComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (args.User is not { Valid: true } user || !IsMoving(user))
            return;

        var spread = GetValues(ent.Comp.Category).MovingSpreadPercent;
        if (spread <= 0)
            return;

        var multiplier = 1f + spread / 100f;
        args.AngleIncrease *= multiplier;
        args.MaxAngle *= multiplier;
    }

    private void OnRefreshMovement(
        Entity<MovementSpeedModifierComponent> ent,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        if (TryGetHeldHandling(ent.Owner, out _, out var handling))
        {
            var heldPenalty = GetValues(handling.Category).HeldPenaltyPercent;
            if (heldPenalty > 0)
                args.ModifySpeed(1f - heldPenalty / 100f);
        }

        if (TryGetShotPenalty(ent.Owner, out _, out var shotValues))
            args.ModifySpeed(1f - shotValues.ShotPenaltyPercent / 100f);
    }
}

public readonly record struct WeaponHandlingValues(
    int HeldPenaltyPercent,
    int ShotPenaltyPercent,
    int MovingSpreadPercent);

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassGunShotMovementComponent : Component
{
    public EntityUid Weapon;
    public Wh40kWeaponHandlingCategory Category;
    public TimeSpan ExpiresAt;
}
