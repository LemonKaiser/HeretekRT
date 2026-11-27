using Content.Shared.Body.Prototypes;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Weapons.Melee.Metabolizer;

/// <summary>
/// Deals bonus melee damage when striking a mob with organs possessing the selected metabolizer type.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BonusDamageOnMetabolismComponent : Component
{
    [DataField]
    public List<ProtoId<MetabolizerTypePrototype>> ExcludedMetabolizers = new();

    [DataField, AutoNetworkedField]
    public ProtoId<MetabolizerTypePrototype>? SelectedMetabolizer;

    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    [DataField]
    public HashSet<MobState> ValidMobStates = [MobState.Alive, MobState.Critical];
}
