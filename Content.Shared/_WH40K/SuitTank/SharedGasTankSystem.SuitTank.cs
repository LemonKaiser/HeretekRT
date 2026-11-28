using Content.Shared._WH40K.SuitTank;
using Content.Shared.Body.Components;

namespace Content.Shared.Atmos.EntitySystems;

public abstract partial class SharedGasTankSystem
{
    [Dependency] private Wh40kSuitTankStorageSystem _suitTank = default!;

    private bool TryGetSuitWearerInternals(EntityUid tank, out EntityUid? internalsUid, out InternalsComponent? internalsComp)
    {
        internalsUid = null;
        internalsComp = null;

        if (!_suitTank.TryGetWearer(tank, out var wearer) || !TryComp(wearer, out internalsComp))
            return false;

        internalsUid = wearer;
        return true;
    }
}
