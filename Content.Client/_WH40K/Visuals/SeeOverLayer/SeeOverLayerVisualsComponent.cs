namespace Content.Client._WH40K.Visuals.SeeOverLayer;

/// <summary>
/// A visual layer whose draw depth changes for viewers with a matching
/// <c>SeeOverLayerComponent</c> layer key.
/// </summary>
[RegisterComponent]
public sealed partial class SeeOverLayerVisualsComponent : Component
{
    [DataField(required: true)]
    public string Layer = string.Empty;

    [DataField]
    public int NormalDrawDepth = (int) Content.Shared.DrawDepth.DrawDepth.Overdoors;

    [DataField]
    public int SeeOverDrawDepth = (int) Content.Shared.DrawDepth.DrawDepth.HighFloorObjects;
}
