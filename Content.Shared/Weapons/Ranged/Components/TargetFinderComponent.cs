using Content.Shared.DeviceLinking;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Tracks a target designated by a <see cref="TargetFinderHitscanComponent"/> ray and relays it to a linked
/// <see cref="TargetAssignComponent"/> weapon.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TargetFinderComponent : Component
{
    [DataField]
    public EntityUid? Target;

    [ViewVariables]
    public EntityUid? TargetAssigner;

    [DataField]
    public ProtoId<SourcePortPrototype> LinkingPort = "TargetSource";
}
