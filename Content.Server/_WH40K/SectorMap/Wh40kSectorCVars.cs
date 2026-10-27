using Robust.Shared.Configuration;

namespace Content.Server._WH40K.SectorMap;

[CVarDefs]
public static class Wh40kSectorCVars
{
    /// <summary>
    /// Trades memory for snapshot/restore work. Disabling this keeps empty systems paused in memory.
    /// </summary>
    public static readonly CVarDef<bool> ColdUnloadEnabled =
        CVarDef.Create("wh40k.sector.cold_unload_enabled", true, CVar.SERVERONLY);
}
