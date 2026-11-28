using Content.Server._WH40K.ShipArmor;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    [Dependency] private Wh40kArmorBackedSystem _wh40kArmorBacked = default!;

    private bool Wh40kArmorHoldsFloor(EntityUid uid)
    {
        return _wh40kArmorBacked.HoldsFloor(uid);
    }
}
