using Content.Shared.Tools.Systems;
using Content.Shared.Wires;

namespace Content.Server.Materials;

public sealed partial class MaterialReclaimerSystem
{
    [Dependency] private SharedToolSystem _wh40kReclaimerTools = default!;

    private bool Wh40kIsPanelTool(EntityUid reclaimer, EntityUid used)
    {
        return HasComp<WiresPanelComponent>(reclaimer)
               && (_wh40kReclaimerTools.HasQuality(used, "Screwing") || _wh40kReclaimerTools.HasQuality(used, "Prying"));
    }
}
