using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Augments;

/// <summary>
/// Continuous power use of an augment while its item toggle is enabled.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(AugmentPowerDrawSystem))]
public sealed partial class AugmentPowerDrawComponent : Component
{
    [DataField(required: true)]
    public float Draw;
}

[ByRefEvent]
public record struct GetAugmentsPowerDrawEvent(EntityUid Body, float TotalDraw = 0f);
