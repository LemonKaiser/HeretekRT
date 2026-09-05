using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Visuals.SeeOverLayer;

/// <summary>
/// Allows the local viewer to render matching visual layers below their mob.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SeeOverLayerComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public HashSet<string> Layers = new();
}
