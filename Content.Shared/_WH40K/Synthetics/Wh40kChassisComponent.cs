using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Synthetics;

/// <summary>
/// Runtime chassis state for Cybernetic (Mechanicus) and IPC bodies: open bay, overclocked servos,
/// thermal buildup, cold stiffness, and reboot cooldown.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kChassisComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool BayOpen;

    [DataField, AutoNetworkedField]
    public bool Overclocked;

    [DataField]
    public float OverclockSpeed = 1.25f;

    [DataField]
    public float BayOpenSpeed = 0.8f;

    [DataField]
    public float BayOpenDamageMultiplier = 1.25f;

    /// <summary>Internal servo heat load (0..100) accumulated by Overclock and dissipated passively.</summary>
    [DataField, AutoNetworkedField]
    public float ChassisHeat;

    /// <summary>Movement slowdown factor (0..0.65) caused by extreme chassis cold.</summary>
    [DataField, AutoNetworkedField]
    public float ColdSlowdown;

    [DataField]
    public TimeSpan NextReboot;

    /// <summary>Server-side sequence counter incremented when an augmentation operating table opens the chassis panel.</summary>
    [ViewVariables]
    public int AugmentFocusSeq;
}

/// <summary>
/// Marks a limb isolated via the chassis panel: its servos are cut and damage penalties are suppressed
/// until restored from the panel.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kIsolatedPartComponent : Component
{
    [DataField]
    public bool CouldEnable = true;
}

/// <summary>
/// Marks a mechanical/prosthetic limb snapped back onto a chassis by hand without surgery.
/// Slows legs/feet and slows DoAfter manipulation on arms/hands until tightened with cable or welded.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kLooseCouplingComponent : Component
{
    [DataField, AutoNetworkedField]
    public int RemainingPasses = 5;

    [DataField]
    public float MoveSpeedModifier = 0.7f;

    [DataField]
    public float DoAfterMultiplier = 1.5f;
}
