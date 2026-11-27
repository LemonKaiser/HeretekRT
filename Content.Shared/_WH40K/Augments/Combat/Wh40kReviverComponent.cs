using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Internal cardiac auto-reviver adapted to HeretekRT's native defibrillation and bloodstream systems.
/// When the host dies (and is neither rotten nor unrevivable), it arrests bleeding, infuses a 60u blood reserve,
/// and delivers up to 3 defibrillation shocks before entering a 10-minute recharge (or 2-minute EMP lockout).
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class Wh40kReviverComponent : Component
{
    [DataField]
    public TimeSpan ChargeTime = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    [DataField]
    public int Shocks = 3;

    [DataField]
    public TimeSpan Recharge = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan EmpLockout = TimeSpan.FromMinutes(2);

    [DataField]
    public float Reserve = 60f;

    [DataField]
    public float InfuseRate = 6f;

    /// <summary>
    /// Healing applied by each internal defibrillation pulse (mirrors standard defibrillator ZapHeal).
    /// </summary>
    [DataField]
    public DamageSpecifier ZapHeal = new()
    {
        DamageDict =
        {
            ["Asphyxiation"] = -40,
            ["Bloodloss"] = -20,
            ["Blunt"] = -10,
            ["Slash"] = -10,
            ["Piercing"] = -10,
        },
    };

    [DataField]
    public SoundSpecifier? ChargeSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_charge.ogg");

    [DataField]
    public SoundSpecifier? ZapSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_zap.ogg");

    [DataField]
    public SoundSpecifier? SuccessSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_success.ogg");

    [DataField]
    public SoundSpecifier? FailureSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_failed.ogg");

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? NextShock;

    [DataField]
    public int ShocksGiven;

    [DataField]
    public float Infused;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ReadyAt;
}
