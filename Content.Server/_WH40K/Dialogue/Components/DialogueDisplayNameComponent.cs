namespace Content.Server._WH40K.Dialogue.Components;

[RegisterComponent]
public sealed partial class DialogueDisplayNameComponent : Component
{
    [DataField("name", required: true)]
    public string Name = string.Empty;
}
