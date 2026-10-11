using Content.Shared._WH40K.SectorMap;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared._NF.Atmos.BUI;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.CriminalRecords;
using Content.Shared.Item;
using Content.Shared.Power;
using Content.Shared.StationRecords;
using Content.Shared.SurveillanceCamera;
using Content.Shared.Tiles;
using Content.Shared.UserInterface;
using Content.Shared.Whitelist;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>Freezes authored infrastructure controls without replacing its normal simulation or UIs.</summary>
public sealed partial class KoronusInfrastructureSystem : EntitySystem
{
    [Dependency] private SharedKoronusInfrastructureSystem _infrastructure = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    private readonly EntityWhitelist _critical = new()
    {
        Components =
        [
            "GravityGenerator", "StationAnchor", "ShipShieldEmitter", "FireControlServer", "FireControlConsole",
            "Apc", "ApcNetSwitch", "PoweredLight", "PowerSupplier", "PowerNetworkBattery", "PortableGenerator", "FuelGenerator",
            "SolarPanel", "SolarControlConsole", "PowerMonitoringConsole",
            "AirAlarm", "AtmosMonitor", "FireAlarm", "AtmosAlertsComputer", "AtmosMonitoringConsole", "GasMiner", "GasPressurePump", "GasVolumePump",
            "GasPressureRegulator", "GasPassiveGate", "GasValve", "GasMixer", "GasFilter", "GasVentPump",
            "GasVentScrubber", "GasOutletInjector", "GasRecycler", "GasThermoMachine",
            "IdCardConsole", "CommunicationsConsole", "GeneralStationRecordConsole", "CriminalRecordsConsole",
            "StationBankATM", "CargoOrderConsole", "CargoShuttleConsole", "ShuttleConsole", "IFFConsole",
            "TelecomServer", "ResearchServer", "SurveillanceCamera", "SurveillanceCameraMonitor",
        ],
    };

    private readonly EntityWhitelist _controlNetwork = new()
    {
        Components = ["Apc", "ApcNetSwitch", "PoweredLight", "AirAlarm", "AtmosAlertsComputer", "GasPressurePump",
            "GasVolumePump", "GasMixer", "GasFilter", "GasVentPump", "GasVentScrubber", "GasOutletInjector"],
    };

    private readonly EntityWhitelist _uiActivation = new()
    {
        Components = ["AirAlarm", "GasVolumePump", "GasMixer", "GasFilter"],
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<BoundUserInterfaceMessageAttempt>(OnUiMessageAttempt);
        SubscribeLocalEvent<KoronusCriticalInfrastructureComponent, BeforePacketSentEvent>(OnBeforePacketSent);
    }

    /// <summary>Called after the facility has loaded and its ProtectedGrid safeguards are installed.</summary>
    public int ProtectGrid(EntityUid grid)
    {
        if (!HasComp<ProtectedGridComponent>(grid)) return 0;
        var count = 0;
        var pending = new Stack<EntityUid>();
        pending.Push(grid);
        while (pending.TryPop(out var uid))
        {
            var transform = Transform(uid);
            var children = transform.ChildEnumerator;
            while (children.MoveNext(out var child)) pending.Push(child);
            // Lock installed structures, not portable player equipment or machine parts in containers.
            if (uid == grid || HasComp<ItemComponent>(uid) && !transform.Anchored || transform.GridUid != grid ||
                !_whitelist.IsWhitelistPass(_critical, uid)) continue;
            var protection = EnsureComp<KoronusCriticalInfrastructureComponent>(uid);
            protection.AllowUiActivation = _whitelist.IsWhitelistPass(_uiActivation, uid);
            Dirty(uid, protection);
            // Some fire-control interfaces normally skip validation; they must use the same server gate.
            if (TryComp<ActivatableUIComponent>(uid, out var ui) && ui.Key is { } key &&
                _ui.TryGetInterfaceData(uid, key, out var data) && !data.RequireInputValidation)
                _ui.SetUi(uid, key, new InterfaceData(data) { RequireInputValidation = true });
            count++;
        }
        return count;
    }

    private void OnBeforePacketSent(EntityUid uid, KoronusCriticalInfrastructureComponent comp, BeforePacketSentEvent args)
    {
        // Authored alarms/switches continue their automatic communication. A player's newly brought
        // controller cannot reconfigure this power/atmos network. Radio and camera traffic is unaffected.
        if (_infrastructure.IsReadOnly(uid) && _whitelist.IsWhitelistPass(_controlNetwork, uid) &&
            !_infrastructure.IsReadOnly(args.Sender))
            args.Cancel();
    }

    private void OnUiMessageAttempt(BoundUserInterfaceMessageAttempt args)
    {
        // A configurator's interface belongs to the handheld tool, not the machine it edits.
        // Keep copying/viewing its buffer available, but reject writes to either protected endpoint.
        if (TryComp<NetworkConfiguratorComponent>(args.Target, out var configurator))
        {
            var changesLinks = args.Message is NetworkConfiguratorClearLinksMessage or
                NetworkConfiguratorToggleLinkMessage or NetworkConfiguratorLinksSaveMessage;
            var changesList = args.Message is NetworkConfiguratorButtonPressedMessage
                { ButtonKey: NetworkConfiguratorButtonKey.Set or NetworkConfiguratorButtonKey.Add or NetworkConfiguratorButtonKey.Clear };
            if (changesLinks && (IsProtected(configurator.ActiveDeviceLink) || IsProtected(configurator.DeviceLinkTarget)) ||
                changesList && IsProtected(configurator.ActiveDeviceList))
                args.Cancel();
        }

        var remoteTarget = args.Message switch
        {
            RemoteGasPressurePumpChangePumpDirectionMessage m => GetEntity(m.Pump),
            RemoteGasPressurePumpChangeOutputPressureMessage m => GetEntity(m.Pump),
            RemoteGasPressurePumpToggleStatusMessage m => GetEntity(m.Pump),
            _ => (EntityUid?) null,
        };
        if (remoteTarget is { } target && _infrastructure.IsReadOnly(target) ||
            _infrastructure.IsReadOnly(args.Target) && !IsReadMessage(args.Message))
            args.Cancel();
    }

    private bool IsProtected(EntityUid? uid)
        => uid is { } target && _infrastructure.IsReadOnly(target);

    private static bool IsReadMessage(BoundUserInterfaceMessage message)
        => message is OpenBoundInterfaceMessage or CloseBoundInterfaceMessage or
            SelectStationRecord or SetStationRecordFilter or CriminalRecordSetStatusFilter or
            PowerMonitoringConsoleMessage or AirAlarmResyncAllDevicesMessage or
            SurveillanceCameraMonitorSwitchMessage or SurveillanceCameraMonitorSubnetRequestMessage or
            SurveillanceCameraRefreshCamerasMessage or SurveillanceCameraRefreshSubnetsMessage or
            SurveillanceCameraDisconnectMessage;
}
