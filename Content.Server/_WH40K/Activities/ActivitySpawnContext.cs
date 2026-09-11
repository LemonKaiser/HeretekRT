using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Activities;

/// <summary>
/// Immutable, server-derived input for a temporary activity grid. It deliberately contains no
/// map path, source grid, dock target, player entity or client-supplied coordinate.
/// </summary>
public readonly record struct ActivitySpawnContext(
    MapId TargetMap,
    string SystemId,
    Vector2 SpawnBand,
    long InstanceId,
    uint Seed);
