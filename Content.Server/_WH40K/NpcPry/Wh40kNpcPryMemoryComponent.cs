namespace Content.Server._WH40K.NpcPry;

/// <summary>Tracks an NPC's recent pry attempts on one door so it gives up instead of retrying every tick.</summary>
[RegisterComponent]
public sealed partial class Wh40kNpcPryMemoryComponent : Component
{
    [ViewVariables]
    public EntityUid? Door;

    [ViewVariables]
    public int Attempts;

    [ViewVariables]
    public TimeSpan LastAttempt;

    [ViewVariables]
    public TimeSpan GiveUpUntil;
}
