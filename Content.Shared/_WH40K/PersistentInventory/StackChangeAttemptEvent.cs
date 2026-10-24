namespace Content.Shared._WH40K.PersistentInventory;

/// <summary>Raised before consuming, splitting or transferring stack units.</summary>
[ByRefEvent]
public struct StackChangeAttemptEvent
{
    public bool Cancelled;
}
