using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server.DeviceNetwork.Components
{
    [RegisterComponent]
    public sealed partial class StationLimitedNetworkComponent : Component
    {
        /// <summary>
        /// The owner of the operational domain the device is limited to.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        public EntityUid? DomainOwner;

        /// <summary>
        /// Whether the entity is allowed to receive packets from entities that are not tied to an operational domain.
        /// </summary>
        [DataField("allowNonStationPackets")]
        [ViewVariables(VVAccess.ReadWrite)]
        public bool AllowNonStationPackets = false;
    }
}
