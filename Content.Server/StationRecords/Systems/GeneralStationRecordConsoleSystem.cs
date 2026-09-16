using System.Linq;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Components;
using Content.Shared.StationRecords;
using Robust.Server.GameObjects;
using Content.Shared.Roles; // Frontier
using Robust.Shared.Prototypes; // Frontier
using Content.Shared.Access.Systems; // Frontier
using Content.Server.Station.Components; // Frontier
using Content.Server._NF.Station.Components; // Frontier
using Content.Server.Administration.Logs; // Frontier
using Content.Shared.Database; // Frontier
using Content.Shared._NF.StationRecords; // Frontier
using Content.Server._WH40K.OperationalDomain;
using Content.Shared._WH40K.OperationalDomain;

namespace Content.Server.StationRecords.Systems;

public sealed partial class GeneralStationRecordConsoleSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private OperationalDomainSystem _operationalDomains = default!;
    [Dependency] private StationRecordsSystem _stationRecords = default!;
    [Dependency] private StationJobsSystem _stationJobsSystem = default!; // Frontier
    [Dependency] private AccessReaderSystem _access = default!; // Frontier
    [Dependency] private IPrototypeManager _proto = default!; // Frontier
    [Dependency] private IAdminLogManager _adminLog = default!; // Frontier

    public override void Initialize()
    {
        SubscribeLocalEvent<GeneralStationRecordConsoleComponent, RecordModifiedEvent>(UpdateUserInterface);
        SubscribeLocalEvent<GeneralStationRecordConsoleComponent, AfterGeneralRecordCreatedEvent>(UpdateUserInterface);
        SubscribeLocalEvent<GeneralStationRecordConsoleComponent, RecordRemovedEvent>(UpdateUserInterface);

        Subs.BuiEvents<GeneralStationRecordConsoleComponent>(GeneralStationRecordConsoleKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(UpdateUserInterface);
            subs.Event<SelectStationRecord>(OnKeySelected);
            subs.Event<SetStationRecordFilter>(OnFiltersChanged);
            subs.Event<DeleteStationRecord>(OnRecordDelete);
            subs.Event<AdjustStationJobMsg>(OnAdjustJob); // Frontier
            subs.Event<SetStationAdvertisementMsg>(OnAdvertisementChanged); // Frontier
        });
    }

    private void OnRecordDelete(Entity<GeneralStationRecordConsoleComponent> ent, ref DeleteStationRecord args)
    {
        if (!ent.Comp.CanDeleteEntries || !IsActorInConsoleDomain(ent.Owner, args.Actor))
            return;

        if (TryGetDomainRecordsOwner(ent.Owner, out var owner, out _))
            _stationRecords.RemoveRecord(new StationRecordKey(args.Id, owner));
        UpdateUserInterface(ent); // Apparently an event does not get raised for this.
    }

    private void UpdateUserInterface<T>(Entity<GeneralStationRecordConsoleComponent> ent, ref T args)
    {
        UpdateUserInterface(ent);
    }

    // TODO: instead of copy paste shitcode for each record console, have a shared records console comp they all use
    // then have this somehow play nicely with creating ui state
    // if that gets done put it in StationRecordsSystem console helpers section :)
    private void OnKeySelected(Entity<GeneralStationRecordConsoleComponent> ent, ref SelectStationRecord msg)
    {
        ent.Comp.ActiveKey = msg.SelectedKey;
        UpdateUserInterface(ent);
    }

    // Frontier: job counts, advertisements
    private void OnAdjustJob(Entity<GeneralStationRecordConsoleComponent> ent, ref AdjustStationJobMsg msg)
    {
        if (!IsActorInConsoleDomain(ent.Owner, msg.Actor))
            return;

        if (_operationalDomains.TryResolveOperationalDomain(ent, out var domain))
        {
            var owner = domain.Owner;
            // Frontier: check access - hack because we don't have an AccessReaderComponent, it's the station
            if (TryComp(owner, out StationJobsComponent? stationJobs) &&
                (stationJobs.Groups.Count > 0 || stationJobs.Tags.Count > 0))
            {
                var accessSources = _access.FindPotentialAccessItems(msg.Actor);
                var access = _access.FindAccessTags(msg.Actor, accessSources);

                // Check access groups and tags
                bool hasAccess = stationJobs.Tags.Any(access.Contains);
                if (!hasAccess)
                {
                    foreach (var group in stationJobs.Groups)
                    {
                        if (!_proto.TryIndex(group, out var accessGroup))
                            continue;

                        hasAccess = accessGroup.Tags.Any(access.Contains);
                        if (hasAccess)
                            break;
                    }
                }

                if (!hasAccess)
                {
                    UpdateUserInterface(ent);
                    return;
                }
            }
            // End Frontier
            if (stationJobs != null)
                _stationJobsSystem.TryAdjustJobSlot(owner, msg.JobProto, msg.Amount, false, true, stationJobs);
            UpdateUserInterface(ent);
        }
    }
    private void OnFiltersChanged(Entity<GeneralStationRecordConsoleComponent> ent, ref SetStationRecordFilter msg)
    {
        if (ent.Comp.Filter == null ||
            ent.Comp.Filter.Type != msg.Type || ent.Comp.Filter.Value != msg.Value)
        {
            ent.Comp.Filter = new StationRecordsFilter(msg.Type, msg.Value);
            UpdateUserInterface(ent);
        }
    }

    private void OnAdvertisementChanged(Entity<GeneralStationRecordConsoleComponent> ent, ref SetStationAdvertisementMsg msg)
    {
        if (IsActorInConsoleDomain(ent.Owner, msg.Actor) &&
            _operationalDomains.TryResolveOperationalDomain(ent, out var domain) &&
            TryComp<ExtraShuttleInformationComponent>(domain.Owner, out var vesselInfo))
        {
            vesselInfo.Advertisement = msg.Advertisement;
            _adminLog.Add(LogType.ShuttleInfoChanged, $"{ToPrettyString(msg.Actor):actor} set their shuttle {ToPrettyString(domain.Owner)}'s ad text to {vesselInfo.Advertisement}");
            UpdateUserInterface(ent);
            _stationJobsSystem.UpdateJobsAvailable(); // Nasty - ideally this sends out partial information - one ship changed its advertisement.
        }
    }
    // End Frontier: job counts, advertisements

    private void UpdateUserInterface(Entity<GeneralStationRecordConsoleComponent> ent)
    {
        var (uid, console) = ent;
        if (!TryGetDomainRecordsOwner(uid, out var owner, out var stationRecords))
        {
            _ui.SetUiState(uid, GeneralStationRecordConsoleKey.Key, new GeneralStationRecordConsoleState(null, null, null, null, console.Filter, ent.Comp.CanDeleteEntries, null));
            return;
        }

        // Frontier: jobs, advertisements
        IReadOnlyDictionary<ProtoId<JobPrototype>, int?>? jobList = null;
        string? advertisement = null;
        if (TryComp(owner, out StationJobsComponent? stationJobs))
        {
            jobList = _stationJobsSystem.GetJobs(owner, stationJobs);
        }

        if (TryComp<ExtraShuttleInformationComponent>(owner, out var extraVessel))
            advertisement = extraVessel.Advertisement;

        if (stationRecords == null)
        {
            _ui.SetUiState(uid, GeneralStationRecordConsoleKey.Key, new GeneralStationRecordConsoleState(null, null, null, jobList, console.Filter, ent.Comp.CanDeleteEntries, advertisement)); // Frontier: add as many args as we can
            return;
        }

        var listing = _stationRecords.BuildListing((owner, stationRecords), console.Filter);

        switch (listing.Count)
        {
            case 0:
                var consoleState = new GeneralStationRecordConsoleState(null, null, null, jobList, console.Filter, ent.Comp.CanDeleteEntries, advertisement); // Frontier: add as many args as we can
                _ui.SetUiState(uid, GeneralStationRecordConsoleKey.Key, consoleState);
                return;
            default:
                if (console.ActiveKey == null)
                    console.ActiveKey = listing.Keys.First();
                break;
        }

        if (console.ActiveKey is not { } id)
        {
            _ui.SetUiState(uid, GeneralStationRecordConsoleKey.Key, new GeneralStationRecordConsoleState(null, null, listing, jobList, console.Filter, ent.Comp.CanDeleteEntries, advertisement)); // Frontier: add as many args as we can
            return;
        }

        var key = new StationRecordKey(id, owner);
        _stationRecords.TryGetRecord<GeneralStationRecord>(key, out var record, stationRecords);

        GeneralStationRecordConsoleState newState = new(id, record, listing, jobList, console.Filter, ent.Comp.CanDeleteEntries, advertisement);
        _ui.SetUiState(uid, GeneralStationRecordConsoleKey.Key, newState);
    }

    private bool TryGetDomainRecordsOwner(
        EntityUid entity,
        out EntityUid owner,
        out StationRecordsComponent? records)
    {
        owner = EntityUid.Invalid;
        records = null;
        if (!_operationalDomains.TryResolveOperationalDomain(entity, out var domain))
            return false;

        owner = domain.Owner;
        if (domain.Kind == OperationalDomainKind.Vessel)
            records = EnsureComp<StationRecordsComponent>(owner);
        else
            records = CompOrNull<StationRecordsComponent>(owner);

        return records != null;
    }

    private bool IsActorInConsoleDomain(EntityUid console, EntityUid actor)
    {
        return _operationalDomains.TryResolveOperationalDomain(console, out var consoleDomain)
               && _operationalDomains.TryResolveOperationalDomain(actor, out var actorDomain)
               && consoleDomain.Owner == actorDomain.Owner;
    }
}
