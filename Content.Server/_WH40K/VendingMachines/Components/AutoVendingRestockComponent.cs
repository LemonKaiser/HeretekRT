namespace Content.Server._WH40K.VendingMachines.Components;

/// <summary>
/// Marks a grid whose vending machines are included in the scheduled station restock.
/// </summary>
[RegisterComponent, Access(typeof(Systems.AutoVendingRestockSystem))]
public sealed partial class AutoVendingRestockComponent : Component
{
    /// <summary>
    /// Last scheduled restock attempt for this grid. Persisting it lets a cold-unloaded
    /// station catch up when its map is restored.
    /// </summary>
    [DataField]
    public TimeSpan LastRestockAt;
}
