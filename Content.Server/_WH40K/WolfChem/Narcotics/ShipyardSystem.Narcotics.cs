using Content.Server._WH40K.WolfChem.Narcotics;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    [Dependency] private Wh40kNarcoticGoodsSystem _wh40kNarcoticGoods = default!;

    /// <summary>
    /// Whether an entity holds narcotics so hull sales pay nothing for it, forcing narcotics to sell at pallets
    /// where seller heat applies.
    /// </summary>
    private bool Wh40kHoldsNarcotics(EntityUid uid)
    {
        return _wh40kNarcoticGoods.StreetValue(uid) > 0;
    }
}
