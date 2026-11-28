using Content.Shared.Inventory.Events;

namespace Content.Shared._WH40K.Clothing;

public abstract partial class SharedWh40kExplosiveBeltSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kExplosiveBeltComponent, BeingUnequippedAttemptEvent>(OnUnequipAttempt);
    }

    private void OnUnequipAttempt(Entity<Wh40kExplosiveBeltComponent> belt, ref BeingUnequippedAttemptEvent args)
    {
        if (belt.Comp.Armed)
            args.Cancel();
    }
}
