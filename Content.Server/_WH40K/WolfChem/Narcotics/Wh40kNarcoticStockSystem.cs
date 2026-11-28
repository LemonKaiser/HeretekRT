using Content.Server._Mono.Radar;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WH40K.WolfChem.Narcotics;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>
/// A ship carrying a narcotics stockpile above <c>wh40k.narcotics.stock_signature</c> broadcasts a radar signature
/// from its largest lot until unloaded.
/// </summary>
public sealed partial class Wh40kNarcoticStockSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private Wh40kNarcoticGoodsSystem _goods = default!;

    public static readonly EntProtoId Signature = "Wh40kNarcoticSignature";
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<EntityUid, (double Total, EntityUid Lot, double LotValue)> _ships = new();
    private readonly HashSet<EntityUid> _shown = new();
    private readonly List<EntityUid> _stale = new();
    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        Scan();
    }

    public double Stock(EntityUid ship)
    {
        return _ships.TryGetValue(ship, out var stock) ? stock.Total : 0;
    }

    public void Scan()
    {
        _ships.Clear();
        _shown.Clear();
        _stale.Clear();

        var threshold = _cfg.GetCVar(Wh40kNarcoticsCVars.StockSignature);
        var query = EntityQueryEnumerator<Wh40kNarcoticStockComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } ship || !HasComp<ShuttleDeedComponent>(ship))
                continue;

            var value = _goods.StreetValue(uid);
            if (value <= 0)
                continue;

            var weight = HasComp<Wh40kNarcoticSignatureComponent>(uid) ? double.MaxValue : value;
            var stock = _ships.GetValueOrDefault(ship);
            _ships[ship] = weight > stock.LotValue
                ? (stock.Total + value, uid, weight)
                : (stock.Total + value, stock.Lot, stock.LotValue);
        }

        if (threshold > 0)
        {
            foreach (var stock in _ships.Values)
            {
                if (stock.Total >= threshold)
                    _shown.Add(stock.Lot);
            }
        }

        var signatures = EntityQueryEnumerator<Wh40kNarcoticSignatureComponent>();
        while (signatures.MoveNext(out var uid, out _))
        {
            if (!_shown.Remove(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            Hide(uid);
        }

        foreach (var uid in _shown)
        {
            Show(uid);
        }
    }

    private void Show(EntityUid uid)
    {
        var signature = EnsureComp<Wh40kNarcoticSignatureComponent>(uid);
        if (HasComp<RadarBlipComponent>(uid)
            || !_prototype.TryIndex<EntityPrototype>(Signature, out var proto)
            || !proto.Components.TryGetValue("RadarBlip", out var blip))
            return;

        EntityManager.AddComponents(uid, new ComponentRegistry { { "RadarBlip", blip } }, removeExisting: false);
        signature.OwnBlip = true;
    }

    private void Hide(EntityUid uid)
    {
        if (TryComp<Wh40kNarcoticSignatureComponent>(uid, out var signature) && signature.OwnBlip)
            RemComp<RadarBlipComponent>(uid);

        RemComp<Wh40kNarcoticSignatureComponent>(uid);
    }
}
