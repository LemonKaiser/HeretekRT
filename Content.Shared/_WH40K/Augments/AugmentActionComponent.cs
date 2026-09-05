using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Augments;

/// <summary>
/// Adds an action to the carrier while this organ is enabled.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentActionComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Action;

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;
}

public sealed partial class AugmentActionEvent : InstantActionEvent;
