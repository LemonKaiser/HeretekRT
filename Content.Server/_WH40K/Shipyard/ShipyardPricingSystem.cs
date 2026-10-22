using Content.Server.Cargo.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Shipyard;

/// <summary>
/// Keeps shipyard prices above the value of the starting equipment without changing prototypes.
/// </summary>
public sealed partial class ShipyardPricingSystem : EntitySystem
{
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private PricingSystem _pricing = default!;

    private readonly Dictionary<string, int> _prices = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>
    /// Appraises each template once, including MapInit contents, and caches the resulting quote.
    /// </summary>
    public bool TryGetPrice(VesselPrototype vessel, out int price)
    {
        if (_prices.TryGetValue(vessel.ID, out price))
            return true;

        _maps.CreateMap(out var mapId);
        try
        {
            if (!_loader.TryLoadGrid(mapId, vessel.ShuttlePath, out var grid))
                return false;

            return TryGetPrice(vessel, grid.Value, out price);
        }
        finally
        {
            _maps.DeleteMap(mapId);
        }
    }

    /// <summary>
    /// Checks an actual delivery and raises the cached quote if its equipment is more valuable.
    /// A cheaper random loadout never lowers an already displayed quote.
    /// </summary>
    public bool TryGetPrice(VesselPrototype vessel, EntityUid grid, out int price)
    {
        price = 0;
        var minimum = Math.Ceiling(_pricing.AppraiseGrid(grid) * Math.Max(1.0, vessel.MinPriceMarkup));
        if (!double.IsFinite(minimum) || minimum > int.MaxValue)
            return false;

        price = Math.Max(vessel.Price, (int) minimum);
        if (_prices.TryGetValue(vessel.ID, out var quotedPrice))
            price = Math.Max(price, quotedPrice);

        _prices[vessel.ID] = price;
        return true;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        // Equipment, materials and reagents can all change a vessel's appraisal.
        _prices.Clear();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _prices.Clear();
    }
}
