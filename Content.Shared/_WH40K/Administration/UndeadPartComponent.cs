using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Administration;

/// <summary>
/// Relays protection of an attached body part to its undead owner.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class UndeadPartComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Body;
}
