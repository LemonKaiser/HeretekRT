using System.Numerics;
using Content.Shared._Mono.Company;

namespace Content.Client._WH40K.Factions;

public sealed class LobbyFactionCatalog : FactionCatalog
{
    protected override IFactionCatalogCard CreateCard() => new LobbyFactionCard();

    protected override IFactionProfileWindow CreateProfile(CompanyPrototype faction) => new LobbyFactionProfileWindow(faction);

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        // Fill the bounded, left-aligned menu column rather than centring it on a wide screen.
        return float.IsFinite(availableSize.X) ? new Vector2(availableSize.X, measured.Y) : measured;
    }
}
