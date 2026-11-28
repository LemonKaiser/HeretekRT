using Content.Shared._WH40K.SuitTank;
using Content.Shared.Atmos.Components;

namespace Content.Shared.Body.Systems;

public abstract partial class SharedInternalsSystem
{
    [Dependency] private Wh40kSuitTankStorageSystem _suitTank = default!;

    private Entity<GasTankComponent>? FindSuitStorageTank(EntityUid user)
    {
        return _suitTank.FindFullestTank(user);
    }
}
