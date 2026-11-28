using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.WornStorage;

[CVarDefs]
public sealed class Wh40kWornStorageCVars
{
    public static readonly CVarDef<bool> ClickOpens =
        CVarDef.Create("wh40k.worn_storage.click_opens", false, CVar.CLIENTONLY | CVar.ARCHIVE);
}
