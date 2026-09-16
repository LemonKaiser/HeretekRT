using System.Linq;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Server._WH40K.OperationalDomain;
using Content.Server.Warps;

namespace Content.Server._NF.Station.Systems;

public sealed partial class StationRenameWarpsSystems : EntitySystem
{
    [Dependency] private OperationalDomainSystem _operationalDomains = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationRenameWarpsComponent, StationRenamedEvent>(OnRenamed);
        SubscribeLocalEvent<StationRenameWarpsComponent, StationPostInitEvent>(OnPostInit);
        SubscribeLocalEvent<EntityRenamedEvent>(OnDomainRenamed);
    }

    private void OnPostInit(EntityUid uid, StationRenameWarpsComponent component, ref StationPostInitEvent args)
    {
        SyncWarpPointsToStation(uid);
    }

    private void OnRenamed(EntityUid uid, StationRenameWarpsComponent component, StationRenamedEvent args)
    {
        SyncWarpPointsToStation(uid);
    }

    public List<Entity<WarpPointComponent>> SyncWarpPointsToStation(EntityUid stationUid, bool? forceAdminOnly = null)
    {
        List<Entity<WarpPointComponent>> ret = new();
        var query = AllEntityQuery<WarpPointComponent>();
        while (query.MoveNext(out var uid, out var warp))
        {
            if (!_operationalDomains.TryResolveOperationalDomain(uid, out var domain) ||
                domain.Owner != stationUid)
                continue;

            if (forceAdminOnly != null)
                warp.AdminOnly = forceAdminOnly.Value;

            if (!warp.UseStationName)
                continue;

            warp.Location = Name(domain.Owner);
            ret.Add((uid, warp));
        }
        return ret;
    }

    public List<Entity<WarpPointComponent>> SyncWarpPointsToStations(IEnumerable<EntityUid> stationUids, bool? forceAdminOnly = null)
    {
        List<Entity<WarpPointComponent>> ret = new();
        var query = AllEntityQuery<WarpPointComponent>();
        while (query.MoveNext(out var uid, out var warp))
        {
            if (!_operationalDomains.TryResolveOperationalDomain(uid, out var domain) ||
                !stationUids.Contains(domain.Owner))
                continue;

            if (forceAdminOnly != null)
                warp.AdminOnly = forceAdminOnly.Value;

            if (!warp.UseStationName)
                continue;

            warp.Location = Name(domain.Owner);
            ret.Add((uid, warp));
        }
        return ret;
    }

    // Grid name functions
    public List<Entity<WarpPointComponent>> SyncWarpPointsToGrid(EntityUid gridUid, bool? forceAdminOnly = null)
    {
        List<Entity<WarpPointComponent>> ret = new();
        var query = AllEntityQuery<WarpPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var warp, out var xform))
        {
            var warpGridUid = xform.GridUid ?? EntityUid.Invalid;

            if (!warpGridUid.Valid || gridUid != warpGridUid)
                continue;

            if (forceAdminOnly != null)
                warp.AdminOnly = forceAdminOnly.Value;

            if (!warp.UseStationName)
                continue;

            warp.Location = _operationalDomains.TryResolveOperationalDomain(uid, out var domain)
                ? Name(domain.Owner)
                : Name(warpGridUid);
            ret.Add((uid, warp));
        }
        return ret;
    }

    public List<Entity<WarpPointComponent>> SyncWarpPointsToGrids(IEnumerable<EntityUid> gridUids, bool? forceAdminOnly = null)
    {
        List<Entity<WarpPointComponent>> ret = new();
        var query = AllEntityQuery<WarpPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var warp, out var xform))
        {
            var warpGridUid = xform.GridUid ?? EntityUid.Invalid;

            if (!warpGridUid.Valid || !gridUids.Contains(warpGridUid))
                continue;

            if (forceAdminOnly != null)
                warp.AdminOnly = forceAdminOnly.Value;

            if (!warp.UseStationName)
                continue;

            warp.Location = _operationalDomains.TryResolveOperationalDomain(uid, out var domain)
                ? Name(domain.Owner)
                : Name(warpGridUid);
            ret.Add((uid, warp));
        }
        return ret;
    }

    /// <summary>
    /// Sets the initial automatic location from the explicit operational owner.
    /// Called by <see cref="WarpPointSystem"/>, which owns the warp startup subscription.
    /// </summary>
    public void SetInitialWarpPointLocation(Entity<WarpPointComponent> warp)
    {
        if (!(warp.Comp.UseStationName || warp.Comp.QueryStationName || warp.Comp.QueryGridName))
            return;

        if (_operationalDomains.TryResolveOperationalDomain(warp, out var domain))
        {
            warp.Comp.Location = Name(domain.Owner);
            return;
        }

        if (warp.Comp.QueryGridName &&
            TryComp(warp, out TransformComponent? transform) &&
            transform.GridUid is { Valid: true } grid)
        {
            warp.Comp.Location = Name(grid);
        }
    }

    private void OnDomainRenamed(ref EntityRenamedEvent ev)
    {
        if (_operationalDomains.TryResolveOperationalDomain(ev.Uid, out var domain) &&
            domain.Owner == ev.Uid)
        {
            SyncWarpPointsToStation(domain.Owner);
        }
    }
}
