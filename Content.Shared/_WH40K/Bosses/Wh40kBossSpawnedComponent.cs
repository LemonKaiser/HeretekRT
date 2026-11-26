namespace Content.Shared._WH40K.Bosses;

[RegisterComponent]
public sealed partial class Wh40kBossSpawnedComponent : Component
{
    // Records that the encounter has already spawned.
    [DataField] public bool Spawned;
}
