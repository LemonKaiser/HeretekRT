using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.GridMomentum;

/// <summary>The grid a mob last stood on, and the velocity it carried away when it floated off.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kGridMomentumComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Grid;

    [DataField, AutoNetworkedField]
    public EntityUid? Left;

    [DataField, AutoNetworkedField]
    public Vector2 Frame;
}
