using Content.Server.Labels;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Server._WH40K.OperationalDomain;
using Content.Shared.Holopad;

namespace Content.Server._NF.Station.Systems;

public sealed partial class StationRenameHolopadsSystem : EntitySystem
{
    [Dependency] private OperationalDomainSystem _operationalDomains = default!;
    [Dependency] private LabelSystem _label = default!; // TODO: use LabelSystem directly instead of this.

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationRenameHolopadsComponent, StationPostInitEvent>(OnPostInit);
        SubscribeLocalEvent<HolopadComponent, ComponentStartup>(OnHolopadStartup);
        SubscribeLocalEvent<EntityRenamedEvent>(OnDomainRenamed);
    }

    private void OnPostInit(EntityUid uid, StationRenameHolopadsComponent component, ref StationPostInitEvent args)
    {
        SyncHolopadsNames(uid);
    }

    private void SyncHolopadsNames(EntityUid stationUid)
    {
        var query = EntityQueryEnumerator<HolopadComponent>();
        while (query.MoveNext(out var uid, out var pad))
        {
            if (!pad.UseStationName)
                continue;

            if (!_operationalDomains.TryResolveOperationalDomain(uid, out var domain) ||
                domain.Owner != stationUid)
                continue;

            SyncHolopad((uid, pad), domain.Owner);
        }
    }

    private void OnHolopadStartup(EntityUid uid, HolopadComponent component, ComponentStartup args)
    {
        SyncHolopad((uid, component));
    }

    private void OnDomainRenamed(ref EntityRenamedEvent ev)
    {
        if (_operationalDomains.TryResolveOperationalDomain(ev.Uid, out var domain) &&
            domain.Owner == ev.Uid)
        {
            SyncHolopadsNames(domain.Owner);
        }
    }

    public void SyncHolopad(Entity<HolopadComponent> holopad, EntityUid? domainOwner = null)
    {
        if (!holopad.Comp.UseStationName)
            return;

        if (domainOwner == null &&
            _operationalDomains.TryResolveOperationalDomain(holopad, out var domain))
        {
            domainOwner = domain.Owner;
        }

        if (domainOwner == null)
        {
            return;
        }

        var padName = "";

        if (!string.IsNullOrEmpty(holopad.Comp.StationNamePrefix))
        {
            padName += holopad.Comp.StationNamePrefix + " ";
        }

        padName += Name(domainOwner.Value);

        if (!string.IsNullOrEmpty(holopad.Comp.StationNameSuffix))
        {
            padName += " " + holopad.Comp.StationNameSuffix;
        }

        _label.Label(holopad, padName);
    }
}
