using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Bosses;

[RegisterComponent]
public sealed partial class Wh40kBossEncounterRuleComponent : Component
{
    [DataField(required: true)] public EntProtoId BossPrototype;
    [DataField] public float Chance = 0.05f;
    [DataField] public float MinimumSpawnDistance = 600f;
    [DataField] public float MaximumSpawnDistance = 900f;
}
