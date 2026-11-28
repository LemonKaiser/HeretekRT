using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Lathe;

/// <summary>
/// Links a lathe or fabrication machine to one parts silo and one chemical silo.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedWh40kFabricationSiloSystem))]
public sealed partial class Wh40kFabricationSiloClientComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? PartsSilo;

    [DataField, AutoNetworkedField]
    public EntityUid? ChemicalSilo;

    [DataField]
    public EntityWhitelist? Parts;
}
