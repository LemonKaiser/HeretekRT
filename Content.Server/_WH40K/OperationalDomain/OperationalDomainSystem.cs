using Content.Server.Station.Systems;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WH40K.OperationalDomain;
using Content.Shared._WH40K.OperationalDomain.Components;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using OperationalDomainData = Content.Shared._WH40K.OperationalDomain.OperationalDomain;

namespace Content.Server._WH40K.OperationalDomain;

/// <summary>
/// Resolves the explicit local owner for operational subsystems.
/// Stations retain their own identity; independent vessel grids only qualify through a vessel marker,
/// <see cref="VesselComponent"/>, or <see cref="ShipOwnershipComponent"/>.
/// </summary>
public sealed class OperationalDomainSystem : EntitySystem
{
    [Dependency] private StationSystem _station = default!;
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>
    /// Marks a map grid as an independent vessel domain owner.
    /// The marker is used for bootstrap vessels which may not have a deed yet.
    /// </summary>
    public bool TryMarkVesselDomain(EntityUid grid)
    {
        if (!IsLiveGrid(grid))
            return false;

        EnsureComp<OperationalDomainVesselComponent>(grid);
        return true;
    }

    /// <summary>
    /// Returns whether a live grid has an explicit vessel identity.
    /// This remains separate from domain resolution because a purchased vessel may correctly
    /// resolve to its own station domain.
    /// </summary>
    public bool IsVesselGrid(EntityUid grid)
    {
        return IsLiveGrid(grid) && IsIndependentVessel(grid);
    }

    /// <summary>
    /// Resolves an entity to its station or independent vessel operational domain.
    /// No map, sector entity, arbitrary station, or arbitrary grid is used as a fallback.
    /// </summary>
    public bool TryResolveOperationalDomain(EntityUid entity, out OperationalDomainData domain)
    {
        domain = default;
        if (!IsLiveEntity(entity) || !TryComp<TransformComponent>(entity, out var transform))
            return false;

        // A station entity itself may be a destination or a stored owner, even though it is not
        // physically parented to its grid. Station ownership otherwise takes precedence over every
        // vessel marker: docking a vessel must not turn station equipment into vessel equipment.
        if (TryComp<StationDataComponent>(entity, out var directStationData))
            return TryCreateStationDomain(entity, directStationData, out domain);

        if (_station.GetOwningStation(entity, transform) is { } station &&
            TryComp<StationDataComponent>(station, out var stationData))
        {
            return TryCreateStationDomain(station, stationData, out domain);
        }

        var grid = TryComp<MapGridComponent>(entity, out _) ? entity : transform.GridUid;
        if (grid is not { } vesselGrid || !IsLiveGrid(vesselGrid) || !IsIndependentVessel(vesselGrid))
            return false;

        domain = new OperationalDomainData(vesselGrid, vesselGrid, OperationalDomainKind.Vessel);
        return true;
    }

    /// <summary>
    /// Checks whether an entity currently belongs to the supplied operational owner.
    /// </summary>
    public bool IsInOperationalDomain(EntityUid entity, OperationalDomainData domain)
    {
        return IsOperationalDomainValid(domain) &&
               TryResolveOperationalDomain(entity, out var resolved) &&
               resolved.Owner == domain.Owner &&
               resolved.Kind == domain.Kind;
    }

    /// <summary>
    /// Gets connected players whose attached entity is in the supplied operational domain.
    /// </summary>
    public IEnumerable<ICommonSession> GetPlayersInOperationalDomain(OperationalDomainData domain)
    {
        if (!IsOperationalDomainValid(domain))
            yield break;

        foreach (var session in Filter.GetAllPlayers(_players))
        {
            if (session.AttachedEntity is not { } entity || !IsInOperationalDomain(entity, domain))
                continue;

            yield return session;
        }
    }

    /// <summary>
    /// Returns whether a domain still has a live owner and primary grid.
    /// </summary>
    public bool IsOperationalDomainValid(OperationalDomainData domain)
    {
        if (!IsLiveEntity(domain.Owner) || !IsLiveGrid(domain.PrimaryGrid))
            return false;

        return domain.Kind switch
        {
            OperationalDomainKind.Station =>
                TryComp<StationDataComponent>(domain.Owner, out var stationData) &&
                _station.GetLargestGrid((domain.Owner, stationData)) == domain.PrimaryGrid,
            OperationalDomainKind.Vessel => domain.Owner == domain.PrimaryGrid && IsIndependentVessel(domain.Owner),
            _ => false,
        };
    }

    private bool IsIndependentVessel(EntityUid grid)
    {
        return HasComp<OperationalDomainVesselComponent>(grid) ||
               HasComp<VesselComponent>(grid) ||
               HasComp<ShipOwnershipComponent>(grid);
    }

    private bool TryCreateStationDomain(
        EntityUid station,
        StationDataComponent stationData,
        out OperationalDomainData domain)
    {
        domain = default;
        if (!IsLiveEntity(station) ||
            _station.GetLargestGrid((station, stationData)) is not { } primaryGrid ||
            !IsLiveGrid(primaryGrid))
        {
            return false;
        }

        domain = new OperationalDomainData(station, primaryGrid, OperationalDomainKind.Station);
        return true;
    }

    private bool IsLiveGrid(EntityUid entity)
    {
        return IsLiveEntity(entity) && HasComp<MapGridComponent>(entity);
    }

    private bool IsLiveEntity(EntityUid entity)
    {
        return entity != EntityUid.Invalid && EntityManager.EntityExists(entity) && !TerminatingOrDeleted(entity);
    }
}
