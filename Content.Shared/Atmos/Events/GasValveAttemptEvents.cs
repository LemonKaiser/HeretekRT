using Robust.Shared.GameObjects;

namespace Content.Shared.Atmos.Events;

/// <summary>
/// Raised before a portable gas tank valve is opened or closed.
/// </summary>
public sealed class GasTankValveAttemptEvent(EntityUid? user, bool open) : CancellableEntityEventArgs
{
    public EntityUid? User { get; } = user;
    public bool Open { get; } = open;
}

/// <summary>
/// Raised before a gas canister release valve is opened or closed.
/// </summary>
public sealed class GasCanisterValveAttemptEvent(EntityUid? user, bool open) : CancellableEntityEventArgs
{
    public EntityUid? User { get; } = user;
    public bool Open { get; } = open;
}
