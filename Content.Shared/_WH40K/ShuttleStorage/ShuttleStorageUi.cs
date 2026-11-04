using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ShuttleStorage;

[Serializable, NetSerializable]
public enum ShuttleStorageUiKey : byte { Key }

[Serializable, NetSerializable]
public enum ShuttleStorageState : byte { Empty, Storing, Stored, Loading, RecoveryRequired }

[Serializable, NetSerializable]
public enum ShuttleStorageResult : byte
{
    Ready, Disabled, Busy, Unavailable, Denied, Stale, Full, Empty, InvalidShip,
    Ownership, NotDocked, Passengers, Size, Complexity, Unsupported, TooLarge,
    NoBerth, Corrupt, RecoveryRequired, Stored, Retrieved, Cooldown, ServiceBusy,
}

[Serializable, NetSerializable]
public sealed record ShuttleStorageShip(NetEntity Entity, string Name, int Width, int Height,
    int Entities, ShuttleStorageResult Result, ShuttleStoragePreview? Preview = null);

[Flags, Serializable, NetSerializable]
public enum ShuttleStorageCell : byte { Empty = 0, Floor = 1, Grating = 2, Wall = 4, Window = 8, Door = 16 }

[Serializable, NetSerializable]
public sealed record ShuttleStoragePreview(int Width, int Height, byte[] Cells)
{
    public bool Valid => ShuttleStorageLimits.FitsDimensions(Width, Height) &&
                         Cells.Length == Width * Height;
}

// Sent to one actor only. Account-specific data must never become a terminal's shared UI state.
[Serializable, NetSerializable]
public sealed class ShuttleStorageSnapshot(long sequence, long revision, ShuttleStorageState state,
    string station, string? name, int width, int height, int bytes, DateTime? savedAt,
    ShuttleStorageShip[] ships, ShuttleStorageResult result, Guid operation,
    ShuttleStoragePreview? preview = null, bool previewPending = false) : BoundUserInterfaceMessage
{
    public readonly long Sequence = sequence;
    public readonly long Revision = revision;
    public readonly ShuttleStorageState State = state;
    public readonly string Station = station;
    public readonly string? Name = name;
    public readonly int Width = width;
    public readonly int Height = height;
    public readonly int Bytes = bytes;
    public readonly DateTime? SavedAt = savedAt;
    public readonly ShuttleStorageShip[] Ships = ships;
    public readonly ShuttleStorageResult Result = result;
    public readonly Guid Operation = operation;
    public readonly ShuttleStoragePreview? Preview = preview;
    public readonly bool PreviewPending = previewPending;
}

[Serializable, NetSerializable]
public sealed class ShuttleStorageRequest(Guid requestId, long revision, NetEntity? ship) : BoundUserInterfaceMessage
{
    public readonly Guid RequestId = requestId;
    public readonly long Revision = revision;
    public readonly NetEntity? Ship = ship;
}

[Serializable, NetSerializable]
public sealed class ShuttleStorageRefresh : BoundUserInterfaceMessage;
