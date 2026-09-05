using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._WH40K.Augments;

public sealed class AugmentRelaySystem : EntitySystem
{
    [Dependency] private readonly AugmentSystem _augment = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<InstalledAugmentsComponent, GetMeleeDamageEvent>(_augment.RelayEvent);
    }
}
