using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.CharacterCreation;

/// <summary>
/// Runtime totals resolved from the account-owned WH40K foundation and permanent purchases.
/// This component is only a replicated combat snapshot for the current mob.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kCharacterStatsComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Melee;

    [DataField, AutoNetworkedField]
    public int Ranged;

    [DataField, AutoNetworkedField]
    public int Endurance;

    [DataField, AutoNetworkedField]
    public int Intelligence;

    [DataField, AutoNetworkedField]
    public int Agility;

}
