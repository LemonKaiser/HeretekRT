using Content.Shared.Actions;

namespace Content.Shared._Mono.PersonalShield;

public sealed partial class PersonalShieldActionEvent : InstantActionEvent;

[ByRefEvent]
public record struct GetPersonalShieldStatsEvent
{
    public float MaxCharge;
    public float RegenRate;
    public float SpinupTime;
    public float BreakCooldown;
    public float PowerDraw;

    public GetPersonalShieldStatsEvent(PersonalShieldSettings settings)
    {
        MaxCharge = settings.MaxCharge;
        RegenRate = settings.RegenRate;
        SpinupTime = settings.SpinupTime;
        BreakCooldown = settings.BreakCooldown;
        PowerDraw = settings.PowerDraw;
    }
}

/// <summary>
/// Raised on a personal shield once it has actually absorbed incoming damage.
/// </summary>
public sealed class PersonalShieldAbsorbedEvent : EntityEventArgs
{
    public float Amount { get; }
    public EntityUid? Origin { get; }

    public PersonalShieldAbsorbedEvent(float amount, EntityUid? origin)
    {
        Amount = amount;
        Origin = origin;
    }
}
