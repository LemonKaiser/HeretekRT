using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Content.Shared._NF.Cloning;

namespace Content.Shared._Mono.Company;

/// <summary>
/// Component that represents a player's affiliated company.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CompanyComponent : Component, ITransferredByCloning
{
    /// <summary>
    /// The name of the company the player belongs to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<CompanyPrototype> CompanyName = "None";
}
