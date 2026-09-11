namespace Content.Shared._WH40K.Activities;

/// <summary>
/// Runtime identity of one interactable physical objective. Its fields are assigned only by the
/// authoritative server after the target has passed safety and content audits.
/// </summary>
[RegisterComponent]
public sealed partial class KoronusActivityObjectiveComponent : Component
{
    [DataField]
    public long InstanceId;

    [DataField]
    public KoronusActivityExecutionKind Execution;
}
