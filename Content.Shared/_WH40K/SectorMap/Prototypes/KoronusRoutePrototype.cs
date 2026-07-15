using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.SectorMap.Prototypes;

/// <summary>
/// A fixed warp corridor between two systems on the Koronus sector map.
/// </summary>
[Prototype("koronusRoute")]
public sealed partial class KoronusRoutePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<KoronusSectorPrototype> Sector;

    [DataField(required: true)]
    public ProtoId<KoronusSystemPrototype> From;

    [DataField(required: true)]
    public ProtoId<KoronusSystemPrototype> To;

    [DataField]
    public bool Bidirectional = true;

    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Optional authored multiplier for the FTL travel phase. When omitted, it is calculated from the
    /// distance between the systems on the sector map.
    /// </summary>
    [DataField]
    public float? TravelTimeMultiplier;

    /// <summary>
    /// UI-only route classification. Navigation rules will interpret it in a later stage.
    /// </summary>
    [DataField]
    public string RouteClass = "Stable";
}
