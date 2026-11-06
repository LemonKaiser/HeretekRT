using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Shipyard.Events;

/// <summary>
///    Launcher vessel preview by loading it on client side on preview the map.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsolePreviewMessage : BoundUserInterfaceMessage
{
    public readonly ProtoId<VesselPrototype> Vessel;

    public ShipyardConsolePreviewMessage(ProtoId<VesselPrototype> vessel)
    {
        Vessel = vessel;
    }
}
