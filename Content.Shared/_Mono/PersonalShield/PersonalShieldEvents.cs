using Content.Shared.Actions;

namespace Content.Shared._Mono.PersonalShield;

public sealed partial class PersonalShieldActionEvent : InstantActionEvent;

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
