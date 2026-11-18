using Content.Shared._Shitmed.Targeting;
using Content.Shared._WH40K.Blood;
using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Durability.Events;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Projectiles;
using Content.Shared.Rejuvenate;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Blood;

public sealed partial class WH40KBulletMarksSystem : EntitySystem
{
    private static readonly TimeSpan MarkLifetime = TimeSpan.FromMinutes(1);
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<HumanoidAppearanceComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<ArmorComponent, ArmorProtectionAppliedEvent>(OnArmorProtection);
        SubscribeLocalEvent<WH40KBulletMarksComponent, RejuvenateEvent>(OnRejuvenate);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KBulletMarksComponent>();
        while (query.MoveNext(out var uid, out var marks))
        {
            if (marks.ExpiresAt is not { } expiresAt)
                continue;

            var remaining = marks.ActiveMarks;
            for (var i = 0; i < expiresAt.Length; i++)
            {
                if (expiresAt[i] <= now)
                    remaining = (byte) (remaining & ~(1 << i));
            }

            UpdateActiveMarks((uid, marks), remaining);
        }
    }

    private void OnDamageChanged(Entity<HumanoidAppearanceComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageDelta is not { } delta)
            return;

        var piercing = delta.DamageDict.GetValueOrDefault("Piercing");
        if (piercing < FixedPoint2.Zero)
        {
            if (TryComp<WH40KBulletMarksComponent>(ent, out var marks))
            {
                var healedAll = args.Damageable.Damage.DamageDict.GetValueOrDefault("Piercing") <= FixedPoint2.Zero;
                HealMarks((ent.Owner, marks), healedAll);
            }
            return;
        }

        if (piercing <= FixedPoint2.Zero || !IsTorsoBullet(args.Tool, args.TargetPart))
            return;

        var projectile = args.Tool!.Value;
        if ((TryGetGarment(ent, "outerClothing", out var garment) || TryGetGarment(ent, "jumpsuit", out garment)) &&
            garment is { } clothing)
        {
            var protective = TryComp<ArmorComponent>(clothing, out var armor) &&
                             (armor.Modifiers.Coefficients.GetValueOrDefault("Piercing", 1f) < 1f ||
                              armor.Modifiers.FlatReduction.GetValueOrDefault("Piercing") > 0f);
            AddMark(clothing, protective ? WH40KBulletMarkMaterial.Armor : WH40KBulletMarkMaterial.Cloth, projectile);
        }

        AddMark(ent, WH40KBulletMarkMaterial.Skin, projectile);
    }

    private void OnArmorProtection(Entity<ArmorComponent> ent, ref ArmorProtectionAppliedEvent args)
    {
        if (args.AbsorbedDamage.DamageDict.GetValueOrDefault("Piercing") <= FixedPoint2.Zero ||
            !IsTorsoBullet(args.Tool, args.TargetPart) || args.Wearer is not { } wearer ||
            !HasComp<HumanoidAppearanceComponent>(wearer))
            return;

        // The visible mark belongs to the exposed garment, including when it stops the bullet.
        if ((TryGetGarment(wearer, "outerClothing", out var garment) || TryGetGarment(wearer, "jumpsuit", out garment)) &&
            garment == ent.Owner)
            AddMark(ent, WH40KBulletMarkMaterial.Armor, args.Tool!.Value);
    }

    private bool IsTorsoBullet(EntityUid? tool, TargetBodyPart? part)
        => part is { } hit && hit != TargetBodyPart.All && (hit & TargetBodyPart.Torso) != 0 &&
           TryComp<ProjectileComponent>(tool, out var projectile) &&
           HasComp<GunComponent>(projectile.Weapon);

    private bool TryGetGarment(EntityUid wearer, string slot, out EntityUid? item)
        => _inventory.TryGetSlotEntity(wearer, slot, out item) && HasComp<ClothingComponent>(item);

    private void AddMark(EntityUid uid, WH40KBulletMarkMaterial material, EntityUid projectile)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var marks = EnsureComp<WH40KBulletMarksComponent>(uid);
        if (marks.LastProjectile == projectile)
            return;

        var expiresAt = marks.ExpiresAt ??= new TimeSpan[WH40KBulletMarksComponent.MaxMarks];
        var now = _timing.CurTime;
        for (var i = 0; i < expiresAt.Length; i++)
        {
            var bit = 1 << i;
            if ((marks.ActiveMarks & bit) != 0 && expiresAt[i] > now)
                continue;

            expiresAt[i] = now + MarkLifetime;
            marks.LastProjectile = projectile;
            marks.Material = material;
            marks.ActiveMarks |= (byte) bit;
            Dirty(uid, marks);
            return;
        }
    }

    private void HealMarks(Entity<WH40KBulletMarksComponent> ent, bool healedAll)
    {
        if (healedAll)
        {
            UpdateActiveMarks(ent, 0);
            return;
        }

        if (ent.Comp.ExpiresAt is not { } expiresAt)
            return;

        var oldest = -1;
        for (var i = 0; i < expiresAt.Length; i++)
        {
            if ((ent.Comp.ActiveMarks & (1 << i)) != 0 && (oldest < 0 || expiresAt[i] < expiresAt[oldest]))
                oldest = i;
        }

        if (oldest >= 0)
            UpdateActiveMarks(ent, (byte) (ent.Comp.ActiveMarks & ~(1 << oldest)));
    }

    private void UpdateActiveMarks(Entity<WH40KBulletMarksComponent> ent, byte remaining)
    {
        if (remaining == ent.Comp.ActiveMarks)
            return;

        if (remaining == 0)
            RemComp<WH40KBulletMarksComponent>(ent);
        else
        {
            ent.Comp.ActiveMarks = remaining;
            Dirty(ent);
        }
    }

    private void OnRejuvenate(Entity<WH40KBulletMarksComponent> ent, ref RejuvenateEvent args)
        => RemComp<WH40KBulletMarksComponent>(ent);
}
