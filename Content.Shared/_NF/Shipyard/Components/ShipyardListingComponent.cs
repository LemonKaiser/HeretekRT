using Content.Shared._Mono.Grid;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;

namespace Content.Shared._NF.Shipyard.Components;

/// <summary>
///   Configures a shipyard console's catalog independently of its UI key.
/// </summary>
[RegisterComponent]
public sealed partial class ShipyardListingComponent : Component
{
    /// <summary>
    ///   All VesselPrototype IDs that should be listed in this shipyard console.
    /// </summary>
    [ViewVariables, DataField(customTypeSerializer: typeof(PrototypeIdListSerializer<VesselPrototype>))]
    public List<string> Shuttles = new();

    /// <summary>
    ///   Vessel groups to include alongside explicit shuttles.
    ///   Null preserves the console's UI group; an empty list disables automatic group listings.
    /// </summary>
    [DataField]
    public List<ShipyardConsoleUiKey>? Groups;

    /// <summary>
    ///   Vessels to hide from this catalog, even if included by a group or explicit listing.
    ///   Vouchers cannot override these exclusions.
    /// </summary>
    [ViewVariables, DataField(customTypeSerializer: typeof(PrototypeIdListSerializer<VesselPrototype>))]
    public List<string> ExcludedShuttles = new();

    /// <summary>
    ///   Grid modifiers to apply to ships bought from here.
    /// </summary>
    [ViewVariables, DataField(customTypeSerializer: typeof(PrototypeIdListSerializer<GridModificationPrototype>))]
    public List<string> Hullmods = new();
}
