namespace Content.Server._WH40K.Cyborgs;

/// <summary>
/// Placed on an AI's remote eye while watching an Overwatch feed so moving the eye cancels the feed.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kAiOverwatchEyeComponent : Component
{
    [ViewVariables]
    public EntityUid Ai;
}
