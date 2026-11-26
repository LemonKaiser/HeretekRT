namespace Content.Server._WH40K.VendingMachines.Components;

/// <summary>
/// Marks a grid whose vending machines are included in the scheduled station restock.
/// </summary>
[RegisterComponent, Access(typeof(Systems.AutoVendingRestockSystem))]
public sealed partial class AutoVendingRestockComponent : Component
{
    /// <summary>
    /// Last scheduled restock attempt for this grid.
    /// </summary>
    [DataField]
    public TimeSpan LastRestockAt;
}
