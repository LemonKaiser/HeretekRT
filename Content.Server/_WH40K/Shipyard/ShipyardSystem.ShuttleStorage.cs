using System.Linq;
using Content.Server._WH40K.ShuttleStorage;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Station;
using Content.Shared.Station.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    public void RemoveStoredShuttleRegistrations(EntityUid grid)
    {
        var deeds = EntityManager.AllEntityQueryEnumerator<ShuttleDeedComponent>();
        var cards = new List<EntityUid>();
        while (deeds.MoveNext(out var entity, out var deed))
        {
            if (entity != grid && deed.ShuttleUid == grid)
                cards.Add(entity);
        }
        foreach (var card in cards)
            RemComp<ShuttleDeedComponent>(card);
        if (_station.GetOwningStation(grid) is not { } station || !TryComp(station, out StationDataComponent? data))
            return;
        if (_operationalDomains.IsVesselGrid(grid) && data.Grids.All(entity => entity == grid))
            _station.DeleteStation(station);
        else
            _station.RemoveGridFromStation(station, grid);
        _shuttleRecordsSystem.RefreshStateForAll(true);
    }

    public void RestoreStoredShuttle(EntityUid grid, NetUserId owner, ICommonSession? session)
    {
        // Serialized station references point at the previous runtime. Create only the vessel's own station.
        RemComp<StationMemberComponent>(grid);
        _station.InitializeNewStation(new StationConfig
        {
            StationPrototype = "StandardFrontierVessel",
            StationComponentOverrides = new(),
        }, [grid], Name(grid));
        var ownership = EnsureComp<ShipOwnershipComponent>(grid);
        ownership.OwnerUserId = owner;
        ownership.IsOwnerOnline = session != null;
        ownership.LastStatusChangeTime = _timing.CurTime;
        ownership.InCryosleep = false;
        if (session != null)
            _shipOwnership.RegisterShipOwnership(grid, session);
        Dirty(grid, ownership);
        var deed = EnsureComp<ShuttleDeedComponent>(grid);
        deed.ShuttleUid = grid;
        deed.DeedHolder = null;
        deed.PurchaseVoucherUid = null;
        _shuttleConsoleLock.RestoreStoredShuttleAccess(grid, session?.AttachedEntity);
        _shuttleRecordsSystem.RefreshStateForAll(true);
    }
}
