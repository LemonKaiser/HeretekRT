using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Augments;

/// <summary>
/// Declares the augmentation body zones and organic/synthetic host compatibility for a WH40K implant organ.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kImplantSpecComponent : Component
{
    /// <summary>
    /// Zones in <see cref="Wh40kAugmentBedZone"/> where this implant can be fitted.
    /// </summary>
    [DataField(required: true)]
    public List<Wh40kAugmentBedZone> Zones = new();

    /// <summary>
    /// If true, the target body part must be synthetic/prosthetic (or belong to a cybernetic/silicon chassis).
    /// </summary>
    [DataField]
    public bool RequiresSyntheticHost;

    /// <summary>
    /// If true, the implant requires organic tissue and cannot be fitted onto a synthetic part or cybernetic chassis.
    /// </summary>
    [DataField]
    public bool RequiresOrganicHost;
}
