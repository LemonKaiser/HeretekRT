using Content.Shared.Item.ItemToggle;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._WH40K.Augments;

public sealed class AugmentStrengthSystem : EntitySystem
{
    [Dependency] private readonly ItemToggleSystem _toggle = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AugmentStrengthComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
    }

    private void OnGetMeleeDamage(Entity<AugmentStrengthComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (_toggle.IsActivated(ent.Owner))
            args.Damage *= ent.Comp.Modifier;
    }
}
