using Content.Server._WH40K.WolfChem.Narcotics;
using Content.Shared.Chemistry.Components;

namespace Content.Server.Botany.Systems;

public sealed partial class BotanySystem
{
    [Dependency] private Wh40kNarcoticGoodsSystem _wh40kNarcoticGoods = default!;

    /// <summary>Marks a grown produce that holds a street drug; harvest fills it without the solution change event.</summary>
    private void Wh40kMarkProduce(EntityUid uid, Solution solution)
    {
        if (_wh40kNarcoticGoods.HoldsNarcotics(solution))
            _wh40kNarcoticGoods.SetNarcotic(uid, true);
    }
}
