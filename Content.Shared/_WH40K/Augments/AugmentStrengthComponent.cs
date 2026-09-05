using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Augments;

[RegisterComponent, NetworkedComponent, Access(typeof(AugmentStrengthSystem))]
public sealed partial class AugmentStrengthComponent : Component
{
    [DataField]
    public float Modifier = 1.25f;
}
