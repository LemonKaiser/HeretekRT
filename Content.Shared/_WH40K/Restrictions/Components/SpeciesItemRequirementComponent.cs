using Content.Shared._WH40K.Restrictions.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Restrictions.Components;

/// <summary>
/// Forces the wearer to use only explicitly compatible armor.
/// Intended for species like Astartes that should not wear standard armor.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SpeciesItemRestrictionSystem))]
public sealed partial class SpeciesItemRequirementComponent : Component
{
    [DataField]
    public bool RequireExplicitArmorCompatibility = true;

    [DataField]
    public string Popup = "species-item-requirement-component-restricted";
}

