namespace Content.Server._WH40K.ChaosDungeon;

/// <summary>Retains the attempt lock across the sector's same-round map save and restore.</summary>
[RegisterComponent]
public sealed partial class ChaosDungeonShipComponent : Component
{
    [DataField]
    public string UsedCycle = string.Empty;

    /// <summary>An active expedition still needs this exact grid for evacuation.</summary>
    public bool ReturnPending;
}
