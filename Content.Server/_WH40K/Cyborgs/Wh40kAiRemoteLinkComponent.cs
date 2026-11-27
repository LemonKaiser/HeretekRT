namespace Content.Server._WH40K.Cyborgs;

/// <summary>
/// Remembers the Station AI brain that last remotely controlled a cyborg so that AI can reconnect off-grid.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kAiRemoteLinkComponent : Component
{
    [DataField]
    public EntityUid? Ai;
}
