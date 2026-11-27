using Content.Shared.Power.Components;

namespace Content.Shared._WH40K.Augments;

/// <summary>
/// Raised locally on a mob to locate the internal battery that should receive power from
/// APCs, loose cells, or rechargers (for example an implanted <see cref="AugmentPowerCellSlotComponent"/>
/// or a cybernetic chassis power cell).
/// </summary>
[ByRefEvent]
public record struct GetDrinkerBatteryEvent(Entity<BatteryComponent>? Battery = null);
