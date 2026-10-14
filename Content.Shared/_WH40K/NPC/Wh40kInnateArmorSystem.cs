using Content.Shared._WH40K.Weapons.Melee;
using Content.Shared.Damage;
using Content.Shared.Item.ItemToggle.Components;

namespace Content.Shared._WH40K.NPC;

public sealed class Wh40kInnateArmorSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<Wh40kInnateArmorComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnDamageModify(Entity<Wh40kInnateArmorComponent> ent, ref DamageModifyEvent args)
    {
        if (ent.Comp.ChainWeaponWeakness &&
            HasComp<WH40KChainswordComponent>(args.Tool) &&
            TryComp<ItemToggleComponent>(args.Tool, out var toggle) && toggle.Activated)
            return;

        args.ConsiderArmorRatingCandidate(ent.Owner, ent.Comp.ArmorRating, int.MaxValue - 1);
    }
}
