namespace Content.Shared._WH40K.Activities;

/// <summary>
/// Runtime identity of the single anchor of a hostile activity. The server assigns every field
/// after spawning; a prototype cannot pre-bind itself to an activity or offer a ghost role.
/// </summary>
[RegisterComponent]
public sealed partial class KoronusActivityThreatAnchorComponent : Component
{
    [DataField]
    public long InstanceId;

    [DataField]
    public KoronusActivityExecutionKind Execution;
}
