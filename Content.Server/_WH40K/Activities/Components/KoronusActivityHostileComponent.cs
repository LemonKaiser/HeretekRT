using System.Numerics;
using Content.Server._WH40K.Activities;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Activities.Components;

/// <summary>
/// Server-only leash record for an NPC spawned by one hostile activity. It never identifies a
/// player, ship or grid as a target; the allowed grid is only the generated surface terrain.
/// </summary>
[RegisterComponent, Access(typeof(KoronusActivityHostileExecutorSystem))]
public sealed partial class KoronusActivityHostileComponent : Component
{
    [ViewVariables]
    public long InstanceId;

    [ViewVariables]
    public string SystemId = string.Empty;

    [ViewVariables]
    public MapId MapId = MapId.Nullspace;

    [ViewVariables]
    public EntityUid? AllowedGrid;

    [ViewVariables]
    public Vector2 AnchorPosition;

    [ViewVariables]
    public float LeashRadius;
}
