namespace Content.Server.Carrying;

/// <summary>Raised on the carried entity before offering or completing a carry.</summary>
public sealed class CarryAttemptEvent(EntityUid carrier, EntityUid carried) : CancellableEntityEventArgs
{
    public EntityUid Carrier { get; } = carrier;
    public EntityUid Carried { get; } = carried;
}
