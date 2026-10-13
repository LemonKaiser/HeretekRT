namespace Content.Server._WH40K.Bosses;

/// <summary>Encounter-owned creatures, removed when their boss dies or they enter protected space.</summary>
[RegisterComponent]
public sealed partial class Wh40kBossMinionComponent : Component
{
    [DataField] public EntityUid? Boss;
    [DataField] public bool SporeMine;
    [ViewVariables] public bool Detonated;
}
