using System;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.PersistentInventory;

[Serializable, NetSerializable]
public sealed record PersistentInventoryAdminSnapshot(Guid Id, string Label);

[Serializable, NetSerializable]
public sealed record PersistentInventoryAdminItem(int Id, string Prototype, string Location, string Details);

[Serializable, NetSerializable]
public sealed class PersistentInventoryAdminEuiState : EuiStateBase
{
    public bool Busy { get; set; }
    public Guid? UserId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public long Revision { get; init; }
    public string Overview { get; init; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public PersistentInventoryAdminSnapshot[] Snapshots { get; init; } = [];
    public Guid? SelectedSnapshot { get; set; }
    public string SnapshotDetails { get; set; } = string.Empty;
    public PersistentInventoryAdminItem[] Items { get; set; } = [];
    public string History { get; init; } = string.Empty;
    public bool CanSave { get; init; }
    public bool CanInvalidate { get; init; }
    public bool CanQuarantine { get; init; }
    public bool CanRollback { get; init; }
    public bool CanRecoverLost { get; init; }
    public bool CanViewLogs { get; init; }
}

[Serializable, NetSerializable]
public enum PersistentInventoryAdminAction : byte
{
    Save,
    Invalidate,
    Quarantine,
    Rollback,
    RecoverLost,
    ReloadCache,
    Validate,
    OpenLogs,
}

[Serializable, NetSerializable]
public sealed class PersistentInventoryAdminSelect(string target) : EuiMessageBase
{
    public string Target { get; } = target;
}

[Serializable, NetSerializable]
public sealed class PersistentInventoryAdminInspect(Guid userId, Guid snapshot) : EuiMessageBase
{
    public Guid UserId { get; } = userId;
    public Guid Snapshot { get; } = snapshot;
}

[Serializable, NetSerializable]
public sealed class PersistentInventoryAdminRequest(Guid userId, long revision,
    PersistentInventoryAdminAction action, Guid? snapshot, string reason, bool force) : EuiMessageBase
{
    public Guid UserId { get; } = userId;
    public long Revision { get; } = revision;
    public PersistentInventoryAdminAction Action { get; } = action;
    public Guid? Snapshot { get; } = snapshot;
    public string Reason { get; } = reason;
    public bool Force { get; } = force;
}
