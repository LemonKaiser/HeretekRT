using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Tactical smartlink implant fitted into a synthetic hand (as an organ or directly on the part):
/// stabilizes recoil, tightens dynamic spread buildup, speeds up spread decay and projectiles,
/// and drives <c>Wh40kSmartlinkOverlay</c>. EMP scrambles it temporarily, worsening recoil/spread.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class Wh40kSmartlinkComponent : Component
{
    [DataField]
    public float IncreaseMultiplier = 0.5f;

    [DataField]
    public float DecayMultiplier = 1.5f;

    [DataField]
    public float ScrambledIncreaseMultiplier = 1.5f;

    [DataField]
    public float ScrambledDecayMultiplier = 0.67f;

    [DataField]
    public float CameraRecoilMultiplier = 0.35f;

    [DataField]
    public float ScrambledCameraRecoilMultiplier = 1.5f;

    [DataField]
    public float ProjectileSpeedMultiplier = 1.2f;

    /// <summary>
    /// Time until which the smartlink is scrambled by an EMP pulse.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan ScrambledUntil = TimeSpan.Zero;
}

/// <summary>
/// Marker placed on a body that has at least one active smartlink in its hands.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kSmartlinkUserComponent : Component;
