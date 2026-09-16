using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Server._WH40K.OperationalDomain;
using Content.Shared.Fax.Components;

namespace Content.Server.Station.Systems;

public sealed partial class StationRenameFaxesSystem : EntitySystem
{
    [Dependency] private OperationalDomainSystem _operationalDomains = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationRenameFaxesComponent, StationRenamedEvent>(OnRenamed);
        SubscribeLocalEvent<StationRenameFaxesComponent, StationPostInitEvent>(OnPostInit);
        SubscribeLocalEvent<EntityRenamedEvent>(OnDomainRenamed);
    }

    private void OnPostInit(EntityUid uid, StationRenameFaxesComponent component, ref StationPostInitEvent args)
    {
        SyncFaxesNames(uid);
    }

    private void OnRenamed(EntityUid uid, StationRenameFaxesComponent component, StationRenamedEvent args)
    {
        SyncFaxesNames(uid);
    }

    private void SyncFaxesNames(EntityUid stationUid)
    {
        var query = EntityQueryEnumerator<FaxMachineComponent>();
        while (query.MoveNext(out var uid, out var fax))
        {
            if (!fax.UseStationName)
                continue;

            if (!_operationalDomains.TryResolveOperationalDomain(uid, out var domain) ||
                domain.Owner != stationUid)
                continue;

            SetFaxName(uid, fax, domain.Owner);
        }
    }

    /// <summary>
    /// Applies the operational-domain owner name when the fax itself finishes map initialization.
    /// Called by <see cref="FaxSystem"/> because that system owns the fax map-init subscription.
    /// </summary>
    public void SyncFaxName(Entity<FaxMachineComponent> fax)
    {
        if (fax.Comp.UseStationName &&
            _operationalDomains.TryResolveOperationalDomain(fax, out var domain))
        {
            SetFaxName(fax, fax.Comp, domain.Owner);
        }
    }

    private void OnDomainRenamed(ref EntityRenamedEvent ev)
    {
        if (_operationalDomains.TryResolveOperationalDomain(ev.Uid, out var domain) &&
            domain.Owner == ev.Uid)
        {
            SyncFaxesNames(domain.Owner);
        }
    }

    private void SetFaxName(EntityUid uid, FaxMachineComponent fax, EntityUid owner)
    {
        var stationName = "";

        if (!string.IsNullOrEmpty(fax.StationNamePrefix))
        {
            stationName += fax.StationNamePrefix + " ";
        }

        stationName += Name(owner);

        if (!string.IsNullOrEmpty(fax.StationNameSuffix))
        {
            stationName += " " + fax.StationNameSuffix;
        }

        fax.FaxName = stationName;
    }
}
