using Content.Shared.Buckle.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._WH40K.HeavyBolter;

/// <summary>
/// Prevents magazine interaction while the emplacement is folded.
/// </summary>
public sealed class SharedWH40KHeavyBolterFoldedInteractionSystem : EntitySystem
{
    private const string HeavyBolterMagazineSlot = "gun_magazine";

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KHeavyBolterComponent, ItemSlotInsertAttemptEvent>(OnItemSlotInsertAttempt);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, ItemSlotEjectAttemptEvent>(OnItemSlotEjectAttempt);
    }

    private void OnItemSlotInsertAttempt(Entity<WH40KHeavyBolterComponent> bolter, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Slot.ID == HeavyBolterMagazineSlot && !IsInteractableForMagazine(bolter))
            args.Cancelled = true;
    }

    private void OnItemSlotEjectAttempt(Entity<WH40KHeavyBolterComponent> bolter, ref ItemSlotEjectAttemptEvent args)
    {
        if (args.Slot.ID == HeavyBolterMagazineSlot && !IsInteractableForMagazine(bolter))
            args.Cancelled = true;
    }

    private bool IsInteractableForMagazine(Entity<WH40KHeavyBolterComponent> bolter)
    {
        return TryComp<StrapComponent>(bolter, out var strap)
            ? strap.Enabled
            : bolter.Comp.Deployed;
    }
}
