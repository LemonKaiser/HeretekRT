namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Adrenal combat booster: raises stamina pool and regen (unless body has Lethargy) and triggers a 1.5s second-wind
/// surge when knocked down by stamina exhaustion (2-minute cooldown).
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class Wh40kAdrenalBoosterComponent : Component
{
    [DataField]
    public float StaminaBonus = 10f;

    [DataField]
    public float RegenerationBonus = 0.6f;

    [DataField]
    public float CooldownMultiplier = 0.75f;

    [ViewVariables]
    public bool StaminaApplied;

    [DataField]
    public TimeSpan DownTime = TimeSpan.FromSeconds(1.5);

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    [ViewVariables, AutoPausedField]
    public TimeSpan? SurgeAt;

    [ViewVariables, AutoPausedField]
    public TimeSpan ReadyAt;

    [ViewVariables]
    public bool Spent;
}
