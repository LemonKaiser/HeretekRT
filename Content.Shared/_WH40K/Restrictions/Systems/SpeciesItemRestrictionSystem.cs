using Content.Shared._WH40K.Restrictions.Components;
using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared._WH40K.Restrictions.Systems;

public sealed class SpeciesItemRestrictionSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SpeciesItemRequirementComponent, IsEquippingAttemptEvent>(OnIsEquippingAttempt);

        SubscribeLocalEvent<SpeciesItemRestrictionComponent, BeingEquippedAttemptEvent>(OnBeingEquippedAttempt);
        SubscribeLocalEvent<SpeciesItemRestrictionComponent, AttemptMeleeEvent>(OnAttemptMelee);
        SubscribeLocalEvent<SpeciesItemRestrictionComponent, SelfBeforeGunShotEvent>(OnBeforeGunShot);
    }

    private void OnIsEquippingAttempt(Entity<SpeciesItemRequirementComponent> ent, ref IsEquippingAttemptEvent args)
    {
        if (args.Cancelled || !ent.Comp.RequireExplicitArmorCompatibility)
            return;

        if (!TryGetSpecies(args.EquipTarget, out var species))
            return;

        if (!HasComp<ArmorComponent>(args.Equipment))
            return;

        if (TryComp<ClothingComponent>(args.Equipment, out var clothing) &&
            (clothing.Slots & args.SlotFlags) == SlotFlags.NONE)
        {
            return;
        }

        if (TryComp<SpeciesItemRestrictionComponent>(args.Equipment, out var restriction) &&
            restriction.RestrictEquip &&
            IsSpeciesAllowed(restriction, species))
        {
            return;
        }

        args.Reason = ent.Comp.Popup;
        args.Cancel();
    }

    private void OnBeingEquippedAttempt(Entity<SpeciesItemRestrictionComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (args.Cancelled || !ent.Comp.RestrictEquip)
            return;

        if (TryComp<ClothingComponent>(ent, out var clothing) &&
            (clothing.Slots & args.SlotFlags) == SlotFlags.NONE)
        {
            return;
        }

        if (!TryGetSpecies(args.EquipTarget, out var species) || IsSpeciesAllowed(ent.Comp, species))
            return;

        args.Reason = ent.Comp.Popup;
        args.Cancel();
    }

    private void OnAttemptMelee(Entity<SpeciesItemRestrictionComponent> ent, ref AttemptMeleeEvent args)
    {
        if (args.Cancelled || !ent.Comp.RestrictMelee)
            return;

        if (!TryGetSpecies(args.User, out var species) || IsSpeciesAllowed(ent.Comp, species))
            return;

        args.Cancelled = true;
        args.Message = Loc.GetString(ent.Comp.Popup);
    }

    private void OnBeforeGunShot(Entity<SpeciesItemRestrictionComponent> ent, ref SelfBeforeGunShotEvent args)
    {
        if (args.Cancelled || args.Gun.Owner != ent.Owner || !ent.Comp.RestrictGun)
            return;

        if (!TryGetSpecies(args.Shooter, out var species) || IsSpeciesAllowed(ent.Comp, species))
            return;

        _popup.PopupClient(Loc.GetString(ent.Comp.Popup), ent, args.Shooter);
        args.Cancel();
    }

    private bool TryGetSpecies(EntityUid uid, out string species)
    {
        species = string.Empty;

        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return false;

        species = humanoid.Species;
        return true;
    }

    private static bool IsSpeciesAllowed(SpeciesItemRestrictionComponent component, string species)
    {
        if (component.Whitelist.Count > 0 && !component.Whitelist.Contains(species))
            return false;

        if (component.Blacklist.Contains(species))
            return false;

        return true;
    }
}

