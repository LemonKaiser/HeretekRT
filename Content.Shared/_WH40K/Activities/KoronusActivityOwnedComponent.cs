namespace Content.Shared._WH40K.Activities;

/// <summary>
/// Marks an entity as belonging to exactly one activity instance. Cleanup must only act on
/// entities carrying the matching instance id.
/// </summary>
[RegisterComponent]
public sealed partial class KoronusActivityOwnedComponent : Component
{
    [DataField(required: true)]
    public long InstanceId;

    [DataField]
    public KoronusActivityOwnedKind Kind;
}

public enum KoronusActivityOwnedKind : byte
{
    Marker,
    Entity,
    Npc,
    Grid,
    Loot,
    Effect,
}
