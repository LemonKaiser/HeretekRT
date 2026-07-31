using Content.Server.Database;
using Robust.Shared.Network;

namespace Content.Server._WH40K.PersistentInventory;

/// <summary>
/// Short-lived server-side entity lock held during a save saga.
/// This component is never serialized into the persistent payload.
/// </summary>
[RegisterComponent]
public sealed partial class PersistentInventoryOperationComponent : Component
{
    public NetUserId UserId;
    public PersistentInventoryOperationId OperationId;
}
