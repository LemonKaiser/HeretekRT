using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Augments;

[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentToolPanelComponent : Component
{
    [DataField]
    public float SwitchCharge = 10f;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentToolPanelActiveItemComponent : Component;

[Serializable, NetSerializable]
public enum AugmentToolPanelUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class AugmentToolPanelSwitchMessage(NetEntity? tool) : BoundUserInterfaceMessage
{
    public NetEntity? DesiredTool = tool;
}
