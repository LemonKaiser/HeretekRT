using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Shared.Atmos.Piping.Components;
using JetBrains.Annotations;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Atmos.Piping.EntitySystems
{
    [UsedImplicitly]
    public sealed partial class AtmosDeviceSystem : EntitySystem
    {
        [Dependency] private IGameTiming _gameTiming = default!;
        [Dependency] private AtmosphereSystem _atmosphereSystem = default!;

        private float _timer;

        // Set of atmos devices that are off-grid but have JoinSystem set.
        private readonly HashSet<Entity<AtmosDeviceComponent>> _joinedDevices = new();

        private static AtmosDeviceDisabledEvent _disabledEv = new();
        private static AtmosDeviceEnabledEvent _enabledEv = new();

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<AtmosDeviceComponent, ComponentInit>(OnDeviceInitialize);
            SubscribeLocalEvent<AtmosDeviceComponent, ComponentShutdown>(OnDeviceShutdown);
            // Re-anchoring should be handled by the parent change.
            SubscribeLocalEvent<AtmosDeviceComponent, EntParentChangedMessage>(OnDeviceParentChanged);
            SubscribeLocalEvent<AtmosDeviceComponent, AnchorStateChangedEvent>(OnDeviceAnchorChanged);
        }

        public void JoinAtmosphere(Entity<AtmosDeviceComponent> ent)
        {
            if (ent.Comp.JoinedGrid != null)
            {
                DebugTools.Assert(HasComp<GridAtmosphereComponent>(ent.Comp.JoinedGrid));
                DebugTools.Assert(Transform(ent).GridUid == ent.Comp.JoinedGrid);
                DebugTools.Assert(ent.Comp.RequireAnchored == Transform(ent).Anchored);
                return;
            }

            var component = ent.Comp;
            var transform = Transform(ent);

            if (component.RequireAnchored && !transform.Anchored)
                return;

            // Attempt to add device to a grid atmosphere.
            bool onGrid = (transform.GridUid != null) && _atmosphereSystem.AddAtmosDevice(transform.GridUid!.Value, ent);

            if (!onGrid && component.JoinSystem)
            {
                _joinedDevices.Add(ent);
                component.JoinedSystem = true;
            }

            component.LastProcess = _gameTiming.CurTime;
            RaiseLocalEvent(ent, ref _enabledEv);
        }

        public void LeaveAtmosphere(Entity<AtmosDeviceComponent> ent)
        {
            var component = ent.Comp;
            // Try to remove the component from an atmosphere, and if not
            if (component.JoinedGrid != null && !_atmosphereSystem.RemoveAtmosDevice(component.JoinedGrid.Value, ent))
            {
                // The grid might have been removed but not us... This usually shouldn't happen.
                component.JoinedGrid = null;
                return;
            }

            if (component.JoinedSystem)
            {
                _joinedDevices.Remove(ent);
                component.JoinedSystem = false;
            }

            component.LastProcess = TimeSpan.Zero;
            RaiseLocalEvent(ent, ref _disabledEv);
        }

        public void RejoinAtmosphere(Entity<AtmosDeviceComponent> component)
        {
            LeaveAtmosphere(component);
            JoinAtmosphere(component);
        }

        private void OnDeviceInitialize(Entity<AtmosDeviceComponent> ent, ref ComponentInit args)
        {
            JoinAtmosphere(ent);
        }

        private void OnDeviceShutdown(Entity<AtmosDeviceComponent> ent, ref ComponentShutdown args)
        {
            LeaveAtmosphere(ent);
        }

        private void OnDeviceAnchorChanged(Entity<AtmosDeviceComponent> ent, ref AnchorStateChangedEvent args)
        {
            // Do nothing if the component doesn't require being anchored to function.
            if (!ent.Comp.RequireAnchored)
                return;

            if (args.Anchored)
                JoinAtmosphere(ent);
            else
                LeaveAtmosphere(ent);
        }

        private void OnDeviceParentChanged(Entity<AtmosDeviceComponent> ent, ref EntParentChangedMessage args)
        {
            RejoinAtmosphere(ent);
        }

        /// <summary>
        /// Update atmos devices that are off-grid but have JoinSystem set. For devices updates when
        /// a device is on a grid, see AtmosphereSystem:UpdateProcessing().
        /// </summary>
        public override void Update(float frameTime)
        {
            _timer += frameTime;

            if (_timer < _atmosphereSystem.AtmosTime)
                return;

            _timer -= _atmosphereSystem.AtmosTime;

            var time = _gameTiming.CurTime;
            var ev = new AtmosDeviceUpdateEvent(_atmosphereSystem.AtmosTime, null, null);
            // Devices can lose Transform before ComponentShutdown when their map/grid is deleted.
            // Never let one stale entry abort the whole atmos tick; remove it and continue with the
            // remaining off-grid devices.
            foreach (var device in _joinedDevices.ToArray())
            {
                if (!Exists(device) ||
                    !TryComp<AtmosDeviceComponent>(device, out var liveComponent) ||
                    !TryComp<TransformComponent>(device, out var transform))
                {
                    _joinedDevices.Remove(device);
                    continue;
                }

                // Refresh the wrapper in case the component was re-added while the old
                // entry was still waiting for ComponentShutdown.
                Entity<AtmosDeviceComponent> liveDevice = (device.Owner, liveComponent);
                var deviceGrid = transform.GridUid;
                if (deviceGrid is { } grid && HasComp<GridAtmosphereComponent>(grid))
                {
                    RejoinAtmosphere(liveDevice);
                    continue;
                }

                RaiseLocalEvent(liveDevice, ref ev);
                liveComponent.LastProcess = time;
            }
        }

        public bool IsJoinedOffGrid(Entity<AtmosDeviceComponent> device)
        {
            return _joinedDevices.Contains(device);
        }
    }
}
