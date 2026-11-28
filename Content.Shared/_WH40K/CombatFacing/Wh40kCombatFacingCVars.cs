using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.CombatFacing;

[CVarDefs]
public sealed class Wh40kCombatFacingCVars
{
    public static readonly CVarDef<bool> FaceCursor =
        CVarDef.Create("wh40k.combat.face_cursor", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);
}
