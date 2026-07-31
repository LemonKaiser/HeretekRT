using System.Collections.Generic;

namespace Content.Server._WH40K.PersistentInventory.Serialization;

public enum PersistentInventoryRootKind
{
    Hand = 0,
    InventorySlot = 1,
}

public sealed record PersistentInventoryRoot(
    PersistentInventoryRootKind Kind,
    string Name,
    int EntityId);

public sealed record PersistentInventoryStorageLocation(
    int X,
    int Y,
    int Direction);

public sealed record PersistentInventoryChild(
    string ContainerId,
    int Index,
    int EntityId,
    PersistentInventoryStorageLocation? StorageLocation = null);

public sealed record PersistentInventoryComponentState(
    string ComponentId,
    IReadOnlyDictionary<string, string> Fields);

public sealed record PersistentInventoryEntityState(
    int EntityId,
    string PrototypeId,
    IReadOnlyList<PersistentInventoryComponentState> Components,
    IReadOnlyList<PersistentInventoryChild> Children);

public sealed record PersistentInventoryPayload(
    int SchemaVersion,
    long CapturedAtUnixMilliseconds,
    string PolicyId,
    int PolicyVersion,
    IReadOnlyList<PersistentInventoryRoot> Roots,
    IReadOnlyList<PersistentInventoryEntityState> Entities);

public sealed record PersistentInventoryLimits(
    int MaxRoots = 64,
    int MaxEntities = 256,
    int MaxDepth = 8,
    int MaxComponentsPerEntity = 16,
    int MaxUncompressedBytes = 4 * 1024 * 1024,
    int MaxCompressedBytes = 1024 * 1024);

public sealed record PackedPersistentInventoryPayload(
    byte[] Data,
    byte[] Sha256,
    int UncompressedBytes,
    int CompressedBytes,
    int EntityCount,
    int RootCount);
