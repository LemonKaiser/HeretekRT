using System.Diagnostics.CodeAnalysis;
using Content.Server._NF.SectorServices;
using Content.Server._NF.ShuttleRecords.Components;
using Content.Server._WH40K.OperationalDomain;
using Content.Server.Administration.Logs;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared._NF.ShuttleRecords;
using Content.Shared.Access.Systems;
using Content.Shared._NF.Shipyard.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._NF.ShuttleRecords;

public sealed partial class ShuttleRecordsSystem : SharedShuttleRecordsSystem
{
    [Dependency] private StationSystem _station = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private SectorServiceSystem _sectorService = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private OperationalDomainSystem _operationalDomains = default!;


    public override void Initialize()
    {
        base.Initialize();
        InitializeShuttleRecords();
    }

    /**
     * Adds a record to the shuttle records list.
     * <param name="record">The record to add.</param>
     */
    public void AddRecord(ShuttleRecord record)
    {
        if (!TryGetShuttleRecordsDataComponent(out var component))
            return;

        record.TimeOfPurchase = _gameTiming.CurTime.Subtract(_gameTicker.RoundStartTimeSpan);
        component.ShuttleRecords[record.EntityUid] = record;
        RefreshStateForAll();
    }

    /**
     * Edits an existing record if one exists for the entity given in the Record
     * <param name="record">The record to update.</param>
     */
    public void TryUpdateRecord(ShuttleRecord record)
    {
        if (!TryGetShuttleRecordsDataComponent(out var component))
            return;

        component.ShuttleRecords[record.EntityUid] = record;
        RefreshStateForAll();
    }

    /**
     * Edits an existing record if one exists for the given entity
     * <param name="record">The record to add.</param>
     */
    public bool TryGetRecord(NetEntity uid, [NotNullWhen(true)] out ShuttleRecord? record)
    {
        if (!TryGetShuttleRecordsDataComponent(out var component) ||
            !component.ShuttleRecords.ContainsKey(uid))
        {
            record = null;
            return false;
        }

        record = component.ShuttleRecords[uid];
        return true;
    }

    private bool TryGetShuttleRecordsDataComponent([NotNullWhen(true)] out SectorShuttleRecordsComponent? component)
    {
        var service = _sectorService.GetServiceEntity();
        if (!service.Valid || !_entityManager.EntityExists(service))
        {
            component = null;
            return false;
        }

        if (_entityManager.EnsureComponent<SectorShuttleRecordsComponent>(service, out var shuttleRecordsComponent))
        {
            component = shuttleRecordsComponent;
            return true;
        }

        component = null;
        return false;
    }

    /// <summary>
    /// The registry remains sector-wide, but a console may be used only by someone in its own
    /// operational domain. This prevents a stale or forged BUI message from operating a terminal
    /// on another vessel.
    /// </summary>
    public bool IsActorInConsoleDomain(EntityUid console, EntityUid actor)
    {
        return _operationalDomains.TryResolveOperationalDomain(console, out var consoleDomain)
               && _operationalDomains.TryResolveOperationalDomain(actor, out var actorDomain)
               && consoleDomain.Owner == actorDomain.Owner;
    }
}
