using Content.Server.Database;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Server._WH40K.PersistentInventory;

/// <summary>
/// Non-persistent server marker for the specific life into which a snapshot was materialized.
/// </summary>
[RegisterComponent, UnsavedComponent]
[Access(typeof(PersistentInventoryLifecycleSystem))]
public sealed partial class PersistentInventoryBoundLifeComponent : Component
{
    public NetUserId UserId;
    public PersistentInventorySnapshotId SnapshotId;
    public PersistentInventoryLifeId LifeId;
    public bool LifeLossStarted;
    public bool SuppressBodyDeletion;
}
