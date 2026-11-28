using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.ShipPa;

/// <summary>
/// Client audio preferences for the ship Vox-PA network.
/// </summary>
[CVarDefs]
public sealed class Wh40kShipPaCVars
{
    public const float MinVolume = 0.1f;

    public static readonly CVarDef<float> Volume =
        CVarDef.Create("wh40k.ship_pa.volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
