using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.ShipArmor;

/// <summary>
/// Server tuning for armor-backed ship hardpoints and thrusters.
/// </summary>
[CVarDefs]
public sealed class Wh40kShipArmorCVars
{
    public static readonly CVarDef<float> Transfer =
        CVarDef.Create("wh40k.ship_armor.transfer", 1.0f, CVar.SERVERONLY);

    public static readonly CVarDef<float> FullThickness =
        CVarDef.Create("wh40k.ship_armor.full_thickness", 12.5f, CVar.SERVERONLY);
}
