using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Factions;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Factions;

public interface IFactionCatalogCard
{
    Control Root { get; }
    event Action? DetailsPressed;
    event Action? ActionPressed;
    void SetFaction(CompanyPrototype faction, string membership, string action, bool disabled, bool selected);
    void UpdateOverview(string overview);
}

public interface IFactionProfileWindow : IDisposable
{
    event Action? ActionPressed;
    void Show();
    void UpdateAction(string membership, string action, string hint, bool disabled);
    void UpdateOverview(string overview);
    void UpdateProfile(FactionProfile profile);
    void UpdateRelations(FactionRelation[] relations);
}
