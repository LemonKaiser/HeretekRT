using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Augments;

/// <summary>
/// An implanted power-cell holder feeding other installed augments.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentPowerCellSlotComponent : Component
{
    [DataField]
    public ProtoId<AlertPrototype> BatteryAlert = "HeretekAugmentBattery";

    [DataField]
    public ProtoId<AlertPrototype> NoBatteryAlert = "BorgBatteryNone";
}

[RegisterComponent, NetworkedComponent]
public sealed partial class HasAugmentPowerCellSlotComponent : Component;

[ByRefEvent]
public record struct AugmentLostPowerEvent(EntityUid Body);

[ByRefEvent]
public record struct AugmentGainedPowerEvent(EntityUid Body);

public sealed partial class AugmentBatteryAlertEvent : BaseAlertEvent;
