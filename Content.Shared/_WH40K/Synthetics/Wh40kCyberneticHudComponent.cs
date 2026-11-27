using Content.Shared._Shitmed.Targeting;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Synthetics;

/// <summary>
/// Networked diagnostics readout state for a Cybernetic (Mechanicus) or IPC body's helmet HUD.
/// Populated by the server from native HeretekRT damage, battery, stamina, body part, temperature,
/// radiation, and chassis states.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kCyberneticHudComponent : Component
{
    public const int MaxFaults = 12;

    [AutoNetworkedField]
    public List<Wh40kCyberneticFault> Faults = new();

    [AutoNetworkedField]
    public float Integrity = 1f;

    [AutoNetworkedField]
    public float Power = -1f;

    [AutoNetworkedField]
    public float Servos = 1f;

    [AutoNetworkedField]
    public float Sensors = 1f;

    [AutoNetworkedField]
    public float CoreTemperature = -1f;

    [AutoNetworkedField]
    public float ChassisHeat;

    [AutoNetworkedField]
    public float RadCollector = -1f;

    [AutoNetworkedField]
    public float Radiation;

    [AutoNetworkedField]
    public bool LowPower;

    [AutoNetworkedField]
    public bool Rebooting;

    [AutoNetworkedField]
    public bool Overclocked;

    [AutoNetworkedField]
    public bool BayOpen;

    [AutoNetworkedField]
    public bool Critical;

    [AutoNetworkedField]
    public bool Dead;

    [AutoNetworkedField]
    public string Advice = string.Empty;

    [AutoNetworkedField]
    public string Banner = string.Empty;
}

[Serializable, NetSerializable]
public readonly record struct Wh40kCyberneticFault(
    TargetBodyPart Part,
    string Line,
    Wh40kCyberneticSeverity Severity,
    string Advice);

[Serializable, NetSerializable]
public enum Wh40kCyberneticSeverity : byte
{
    Info,
    Warn,
    Crit,
    Fail,
}

public enum Wh40kCyberneticHudTier : byte
{
    None,
    Light,
    Moderate,
    Heavy,
}

public static class Wh40kCyberneticHudHelper
{
    public const float HeavyAt = 0.65f;

    public static float ComputeStrain(Wh40kCyberneticHudComponent hud)
    {
        var damageStrain = 1f - Math.Clamp(hud.Integrity, 0f, 1f);
        var powerStrain = hud.LowPower ? 0.55f : hud.Power >= 0f && hud.Power < 0.25f ? (0.25f - hud.Power) * 1.6f : 0f;
        var heatStrain = hud.ChassisHeat >= 0.5f ? (hud.ChassisHeat - 0.5f) * 1.4f : 0f;
        var critStrain = hud.Critical ? 0.9f : 0f;
        return Math.Clamp(MathF.Max(MathF.Max(damageStrain, powerStrain), MathF.Max(heatStrain, critStrain)), 0f, 1f);
    }

    public static Wh40kCyberneticHudTier ComputeTier(Wh40kCyberneticHudComponent hud)
    {
        var strain = ComputeStrain(hud);
        var worst = Wh40kCyberneticSeverity.Info;
        foreach (var fault in hud.Faults)
        {
            if (fault.Severity > worst)
                worst = fault.Severity;
        }

        if (hud.Critical || strain >= HeavyAt || worst == Wh40kCyberneticSeverity.Fail)
            return Wh40kCyberneticHudTier.Heavy;

        if (strain >= 0.28f || worst >= Wh40kCyberneticSeverity.Warn || hud.Faults.Count > 0)
            return Wh40kCyberneticHudTier.Moderate;

        if (strain >= 0.08f || hud.Overclocked || hud.BayOpen)
            return Wh40kCyberneticHudTier.Light;

        return Wh40kCyberneticHudTier.None;
    }

    public static string PartLocKey(TargetBodyPart part) => part switch
    {
        TargetBodyPart.Head => "wh40k-chassis-part-head",
        TargetBodyPart.Torso or TargetBodyPart.Groin => "wh40k-chassis-part-torso",
        TargetBodyPart.LeftArm => "wh40k-chassis-part-left-arm",
        TargetBodyPart.RightArm => "wh40k-chassis-part-right-arm",
        TargetBodyPart.LeftHand => "wh40k-chassis-part-left-hand",
        TargetBodyPart.RightHand => "wh40k-chassis-part-right-hand",
        TargetBodyPart.LeftLeg => "wh40k-chassis-part-left-leg",
        TargetBodyPart.RightLeg => "wh40k-chassis-part-right-leg",
        TargetBodyPart.LeftFoot => "wh40k-chassis-part-left-foot",
        TargetBodyPart.RightFoot => "wh40k-chassis-part-right-foot",
        _ => "wh40k-chassis-part-chassis",
    };
}
