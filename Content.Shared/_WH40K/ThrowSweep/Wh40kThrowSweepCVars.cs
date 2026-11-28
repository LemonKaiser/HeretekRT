using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.ThrowSweep;

/// <summary>
/// Settings preventing fast thrown entities from tunneling through walls.
/// </summary>
[CVarDefs]
public sealed class Wh40kThrowSweepCVars
{
    public static readonly CVarDef<float> SweepSpeed =
        CVarDef.Create("wh40k.throw_sweep_speed", 6f, CVar.SERVERONLY);

    public static readonly CVarDef<float> ImpactThrowMax =
        CVarDef.Create("wh40k.impact_throw_max", 12f, CVar.SERVERONLY);
}
