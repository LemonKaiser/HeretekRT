using Content.Server.DeviceNetwork.Components;
using Content.Server._WH40K.OperationalDomain;
using Content.Shared.DeviceNetwork.Events;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Server.DeviceNetwork.Systems
{
    /// <summary>
    /// This system requires the StationLimitedNetworkComponent to be on the the sending entity as well as the receiving entity
    /// </summary>
    [UsedImplicitly]
    public sealed partial class StationLimitedNetworkSystem : EntitySystem
    {
        [Dependency] private OperationalDomainSystem _operationalDomains = default!;
        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<StationLimitedNetworkComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<StationLimitedNetworkComponent, BeforePacketSentEvent>(OnBeforePacketSent);
        }

        /// <summary>
        /// Sets the operational domain owner the device is limited to.
        /// </summary>
        public void SetDomainOwner(EntityUid uid, EntityUid? domainOwner, StationLimitedNetworkComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            component.DomainOwner = domainOwner;
        }

        /// <summary>
        /// Resolves and stores the owner of the device's current operational domain.
        /// </summary>
        public bool TrySetDomainOwner(EntityUid uid, StationLimitedNetworkComponent? component = null)
        {
            if (!Resolve(uid, ref component) ||
                !_operationalDomains.TryResolveOperationalDomain(uid, out var domain))
                return false;

            component.DomainOwner = domain.Owner;
            return true;
        }

        /// <summary>
        /// Set the domain owner when the component is added.
        /// </summary>
        private void OnMapInit(EntityUid uid, StationLimitedNetworkComponent networkComponent, MapInitEvent args)
        {
            TrySetDomainOwner(uid, networkComponent);
        }

        /// <summary>
        /// Checks if both devices are limited to the same operational domain.
        /// </summary>
        private void OnBeforePacketSent(EntityUid uid, StationLimitedNetworkComponent component, BeforePacketSentEvent args)
        {
            if (!TrySetDomainOwner(uid, component))
            {
                args.Cancel();
                return;
            }

            if (!CheckDomainOwner(args.Sender, component.AllowNonStationPackets, component.DomainOwner))
            {
                args.Cancel();
            }
        }

        /// <summary>
        /// Compares the domain owners of the sending and receiving network components.
        /// Returns false if either cannot resolve to a domain or their owners differ.
        /// Returns true for a non-limited sender when `allowNonStationPackets` is set.
        /// </summary>
        private bool CheckDomainOwner(EntityUid senderUid, bool allowNonStationPackets, EntityUid? receiverDomainOwner, StationLimitedNetworkComponent? sender = null)
        {
            if (!receiverDomainOwner.HasValue)
                return false;

            if (!Resolve(senderUid, ref sender, false))
                return allowNonStationPackets;

            if (!TrySetDomainOwner(senderUid, sender))
                return false;

            return sender.DomainOwner == receiverDomainOwner;
        }
    }
}
