using Content.Server._WH40K.SectorMap.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Shuttles;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

/// <summary>
/// Console entry points for planetary transfers. They resolve the actual piloted grid first and
/// delegate all validation and movement to the sector's server-only planetary runtime.
/// </summary>
public sealed partial class ShuttleConsoleSystem
{
    private void OnPlanetaryLandingRequest(
        Entity<ShuttleConsoleComponent> entity,
        ref ShuttleConsolePlanetaryLandingRequestMessage args)
    {
        if (!TryGetConsoleShuttle(entity.Owner, out var shuttleGrid))
            return;

        if (!_koronusPlanetary.TryLand(
                shuttleGrid,
                args.CelestialBodyId,
                args.LandingSiteId,
                out var failure,
                args.Actor))
        {
            _popup.PopupEntity(
                Loc.GetString("koronus-planetary-landing-denied", ("reason", GetPlanetaryFailureText(failure))),
                entity.Owner,
                PopupType.Medium);
            RefreshShuttleConsoles();
            return;
        }

        // Occupancy is visible to every open console, not only to the shuttle that initiated landing.
        RefreshShuttleConsoles();
    }

    private void OnPlanetaryLaunchRequest(
        Entity<ShuttleConsoleComponent> entity,
        ref ShuttleConsolePlanetaryLaunchRequestMessage args)
    {
        if (!TryGetConsoleShuttle(entity.Owner, out var shuttleGrid))
            return;

        if (!_koronusPlanetary.TryLaunch(shuttleGrid, out var failure))
        {
            _popup.PopupEntity(
                Loc.GetString("koronus-planetary-launch-denied", ("reason", GetPlanetaryFailureText(failure))),
                entity.Owner,
                PopupType.Medium);
            RefreshShuttleConsoles();
            return;
        }

        RefreshShuttleConsoles();
    }

    private bool TryGetConsoleShuttle(EntityUid console, out EntityUid shuttleGrid)
    {
        shuttleGrid = EntityUid.Invalid;
        var droneConsole = GetDroneConsole(console);
        return droneConsole != null &&
               _xformQuery.TryGetComponent(droneConsole.Value, out var transform) &&
               transform.GridUid is { } grid &&
               HasComp<ShuttleComponent>(grid) &&
               (shuttleGrid = grid).IsValid();
    }

    private string GetPlanetaryFailureText(KoronusPlanetaryTransferFailure failure)
    {
        return Loc.GetString($"koronus-planetary-failure-{failure.ToString().ToLowerInvariant()}");
    }
}
