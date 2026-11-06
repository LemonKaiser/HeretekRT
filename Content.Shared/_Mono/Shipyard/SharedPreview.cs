using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Mono.Shipyard;

public sealed class SharedPreview
{
    [Serializable, NetSerializable]
    public enum ShipyardPreviewUiKey : byte
    {
        Key
    }

    [Serializable, NetSerializable]
    public sealed class ShipyardPreviewUserInterfaceState : BoundUserInterfaceState
    {
        public readonly ProtoId<VesselPrototype> Vessel;

        public ShipyardPreviewUserInterfaceState(ProtoId<VesselPrototype> vessel)
        {
            Vessel = vessel;
        }
    }

    [Serializable, NetSerializable]
    public sealed class ShipyardPreviewExitMessage : BoundUserInterfaceMessage
    {

    }
}
