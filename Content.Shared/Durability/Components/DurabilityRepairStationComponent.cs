using Content.Shared.DoAfter;
using Robust.Shared.GameStates;

namespace Content.Shared.Durability.Components;

/// <summary>
/// Configuration and runtime state of a durability repair station.
/// The repairable entity is stored in the station's single internal item slot.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DurabilityRepairStationComponent : Component
{
    public const string RepairSlotId = "repair";

    /// <summary>
    /// Minimum workbench tier represented by this station.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Tier = 1;

    /// <summary>
    /// Kept as station data for compatibility with existing prototypes. The station always has one slot.
    /// </summary>
    [DataField]
    public int MaxRepairSlots = 1;

    /// <summary>
    /// Durability restored by one material unit when an item does not override it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float RepairPerMaterial;

    /// <summary>
    /// Duration of one repair cycle in seconds when it is not overridden by the prototype.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float RepairDurationSeconds;

    /// <summary>
    /// T3 stations require an APC power receiver and an active power connection.
    /// </summary>
    [DataField]
    public bool RequirePower;

    [ViewVariables]
    public DoAfterId? ActiveDoAfter;

    [ViewVariables]
    public EntityUid? ActiveUser;

    [ViewVariables]
    public EntityUid? ActiveItem;

    [ViewVariables]
    public EntityUid? ActiveTool;

    [ViewVariables]
    public EntityUid? ActiveMaterial;
}
