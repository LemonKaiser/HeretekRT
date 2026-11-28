using Content.Shared._WH40K.SuitTank;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class GasTankSystem
{
    [Dependency] private Wh40kSuitTankStorageSystem _suitTankStorage = default!;

    private bool InUsersSuit(EntityUid tank, EntityUid? user)
    {
        return user != null && _suitTankStorage.TryGetWearer(tank, out var wearer) && wearer == user;
    }
}
