using Content.Shared.Standing;
using Content.Shared.Throwing;

namespace Content.Shared._WH40K.Weapons.Melee;

/// <summary>
/// Keeps the power fist attached during falls and blocks throw-style disarm interactions.
/// </summary>
public sealed class SharedWH40KPowerFistSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPowerFistComponent, FellDownThrowAttemptEvent>(OnFellDownThrowAttempt);
        SubscribeLocalEvent<WH40KPowerFistComponent, ThrowItemAttemptEvent>(OnThrowItemAttempt);
    }

    private void OnFellDownThrowAttempt(Entity<WH40KPowerFistComponent> ent, ref FellDownThrowAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnThrowItemAttempt(Entity<WH40KPowerFistComponent> ent, ref ThrowItemAttemptEvent args)
    {
        args.Cancelled = true;
    }
}
