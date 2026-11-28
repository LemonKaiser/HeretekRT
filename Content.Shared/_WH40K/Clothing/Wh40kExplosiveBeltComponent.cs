using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Clothing;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kExplosiveBeltComponent : Component
{
    [AutoNetworkedField]
    public bool Armed;
}

public sealed partial class Wh40kArmExplosiveBeltEvent : InstantActionEvent;
