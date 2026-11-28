using Content.Server.Explosion.EntitySystems;
using Content.Shared._WH40K.Clothing;
using Content.Shared.Inventory;
using Robust.Shared.Audio;

namespace Content.Server._WH40K.Clothing;

public sealed partial class Wh40kExplosiveBeltSystem : SharedWh40kExplosiveBeltSystem
{
    private static readonly SoundPathSpecifier CountdownSound = new("/Audio/Machines/Nuke/general_beep.ogg");

    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kExplosiveBeltComponent, Wh40kArmExplosiveBeltEvent>(OnArm);
    }

    private void OnArm(Entity<Wh40kExplosiveBeltComponent> belt, ref Wh40kArmExplosiveBeltEvent args)
    {
        if (args.Handled || belt.Comp.Armed ||
            !_inventory.TryGetSlotEntity(args.Performer, "belt", out var equipped) || equipped != belt.Owner)
        {
            return;
        }

        belt.Comp.Armed = true;
        Dirty(belt);
        _trigger.HandleTimerTrigger(belt, args.Performer, delay: 10, beepInterval: 1, initialBeepDelay: 0, beepSound: CountdownSound);
        args.Handled = true;
    }
}
