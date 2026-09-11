using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Activities.Components;

/// <summary>
/// Runtime record attached only to the root of a generated activity grid. The grid must remain a
/// static disposable set piece on this exact system map until the director removes it.
/// </summary>
[RegisterComponent, Access(typeof(KoronusActivityGridOpportunityExecutorSystem))]
public sealed partial class KoronusActivityGridOpportunityComponent : Component
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
}
