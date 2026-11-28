using Content.Shared._WH40K.Synthetics;

namespace Content.Shared._WH40K.WolfChem.Neuroware;

/// <summary>
/// Raised on a body with <see cref="Wh40kCyberneticHudComponent"/> during HUD synchronization so subsystems such as
/// Neuroware can append fault readouts without coupling directly to chassis state logic.
/// </summary>
[ByRefEvent]
public readonly record struct Wh40kCyberneticHudCollectEvent(List<Wh40kCyberneticFault> Faults);
