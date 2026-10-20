namespace Content.Shared.Mobs.Events;

/// <summary>
/// Raised before leaving the current mob state or applying any effects of the new state.
/// </summary>
public sealed class MobStateChangingEvent(MobState newState) : CancellableEntityEventArgs
{
    public readonly MobState NewState = newState;
}
