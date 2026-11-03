using Robust.Shared.Network;

namespace Content.Server._WH40K.Cleanup;

/// <summary>
/// Allows a body's persistence system to finish its durable cleanup before removing it from the world.
/// </summary>
[ByRefEvent]
public record struct DisconnectedPlayerCleanupEvent(NetUserId UserId, bool Handled = false);
