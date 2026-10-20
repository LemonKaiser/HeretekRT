using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Bosses;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kMarineMacePartComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid? Boss;
    [DataField, AutoNetworkedField] public bool Head;
}
