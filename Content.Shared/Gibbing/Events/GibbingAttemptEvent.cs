namespace Content.Shared.Gibbing.Events;

/// <summary>
/// Raised before gibbing a body or entity, burning a part, or disturbing its contents.
/// </summary>
public sealed class GibbingAttemptEvent : CancellableEntityEventArgs
{
}
