namespace Content.Shared.Inventory;

public abstract partial class InventorySystem
{
    public void TriggerHandContactInteraction(EntityUid uid)
    {
        foreach (var item in _handsSystem.EnumerateHeld(uid))
            _interactionSystem.DoContactInteraction(uid, item, null, true);
    }
}
