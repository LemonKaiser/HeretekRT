using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Augments;

/// <summary>
/// Marks an organ as a cybernetic augment. The body that contains it is resolved by
/// <see cref="AugmentSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentComponent : Component;

/// <summary>
/// Tracks augments currently installed in a body so shared body events can be relayed to them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class InstalledAugmentsComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<NetEntity> InstalledAugments = new();
}
