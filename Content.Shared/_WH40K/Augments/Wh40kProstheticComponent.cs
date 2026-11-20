using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Augments;

[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kProstheticComponent : Component
{
    [DataField(required: true)]
    public int BonusPercent;
}
