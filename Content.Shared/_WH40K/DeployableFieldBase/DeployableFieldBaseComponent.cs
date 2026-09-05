using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.DeployableFieldBase;

/// <summary>
/// A one-use capsule that deploys a small field-base grid on a Koronus surface.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DeployableFieldBaseComponent : Component
{
    [DataField]
    public float DeployTime = 5f;

    [DataField]
    public Vector2i Size = new(5, 5);
}
