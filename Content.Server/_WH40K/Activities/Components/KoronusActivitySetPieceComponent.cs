using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Activities.Components;

/// <summary>
/// Server-only identity of one loaded authored stage-two location. It is installed only after the
/// grid passed its static-content audit and is used to reject moved, repurposed or duplicate roots.
/// </summary>
[RegisterComponent, Access(typeof(KoronusActivitySetPieceExecutorSystem))]
public sealed partial class KoronusActivitySetPieceComponent : Component
{
    [ViewVariables]
    public long InstanceId;

    [ViewVariables]
    public string SystemId = string.Empty;

    [ViewVariables]
    public MapId MapId = MapId.Nullspace;

    [ViewVariables]
    public Vector2 SpawnPosition;

    [ViewVariables]
    public float LeashRadius;

    [ViewVariables]
    public EntityUid? AllowedSurfaceGrid;
}
