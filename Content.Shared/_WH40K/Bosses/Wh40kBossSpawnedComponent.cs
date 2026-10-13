namespace Content.Shared._WH40K.Bosses;

[RegisterComponent]
public sealed partial class Wh40kBossSpawnedComponent : Component
{
    // Saved with a cold-unloaded map, so a defeated encounter is not spawned again on restore.
    [DataField] public bool Spawned;
}
