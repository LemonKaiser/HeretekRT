using Content.Shared.Humanoid.Prototypes;
using Content.Shared._WH40K.Restrictions.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Restrictions.Components;

/// <summary>
/// Restricts equipping or using an item to specific species.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SpeciesItemRestrictionSystem))]
public sealed partial class SpeciesItemRestrictionComponent : Component
{
    [DataField]
    public List<ProtoId<SpeciesPrototype>> Whitelist = new();

    [DataField]
    public List<ProtoId<SpeciesPrototype>> Blacklist = new();

    [DataField]
    public bool RestrictEquip = true;

    [DataField]
    public bool RestrictMelee;

    [DataField]
    public bool RestrictGun;

    [DataField]
    public string Popup = "species-item-restriction-component-restricted";
}

