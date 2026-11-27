using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Animation;

/// <summary>
/// Animates an entity's sprite offset (and optional slope rotation) along a sine wave.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SineWaveAnimationComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan AnimationLength = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public float KeyFrames = 60;

    [DataField, AutoNetworkedField]
    public bool ResetOffsetOnEnd = true;

    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2 StartOffset;

    [DataField, AutoNetworkedField]
    public bool ResetRotationOnEnd = true;

    [ViewVariables(VVAccess.ReadWrite)]
    public Angle StartRotation;

    [DataField, AutoNetworkedField]
    public bool Repeat = true;

    [DataField, AutoNetworkedField]
    public SignWaveDefinition? XWave;

    [DataField, AutoNetworkedField]
    public SignWaveDefinition? YWave;

    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan TotalTimeX;

    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan TotalTimeY;
}

/// <summary>
/// Sine wave axis definition: Period = 1 / Frequency, offset = Amplitude * Sin((time + PhaseOffset) / Period).
/// </summary>
[Serializable, NetSerializable, DataDefinition]
public partial struct SignWaveDefinition
{
    [DataField]
    public float Amplitude = 1;

    [DataField]
    public float Frequency = 1;

    public TimeSpan Period => TimeSpan.FromSeconds(1 / Frequency);

    [DataField]
    public TimeSpan? PhaseOffset = TimeSpan.Zero;

    [DataField]
    public bool RotateToFollowSlope = true;
}
