using System.Numerics;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Shared._WH40K.Activities;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Server.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Activities;

/// <summary>
/// Central hard-reject policy for future activity executors. It intentionally fails closed: an
/// unknown map, system, grid or movement state is never an eligible activity target.
/// </summary>
public sealed class ActivitySafetyGuard : EntitySystem
{
    private const KoronusSafetyRule ProtectedAreaRules =
        KoronusSafetyRule.PlayerDamage |
        KoronusSafetyRule.StationProtection |
        KoronusSafetyRule.ShipWeapons;

    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private KoronusSafetyPolicySystem _safety = default!;
    [Dependency] private MapSystem _maps = default!;

    public bool TryValidate(
        in KoronusActivityTarget target,
        out KoronusActivityRejectReason reason)
    {
        if (target.ShuttleInFtl)
        {
            reason = KoronusActivityRejectReason.ShuttleInFtl;
            return false;
        }

        if (target.PlanetaryTransit)
        {
            reason = KoronusActivityRejectReason.PlanetaryTransit;
            return false;
        }

        if (!_sector.TryGetSystemPrototype(target.SystemId, out KoronusSystemPrototype system))
        {
            reason = KoronusActivityRejectReason.UnknownSystem;
            return false;
        }

        if (!system.Enabled)
        {
            reason = KoronusActivityRejectReason.DisabledSystem;
            return false;
        }

        if (!KoronusActivityRuntimePolicy.IsSystemEligible(system.ID, system.Enabled, system.ActivityEligible))
        {
            reason = KoronusActivityRejectReason.NotActivityEligible;
            return false;
        }

        if (!_sector.TryGetSystemMap(target.SystemId, out var mapId))
        {
            reason = KoronusActivityRejectReason.UnloadedSystem;
            return false;
        }

        var isSystemMap = target.MapId == mapId;
        var isMatchingSurface = !isSystemMap &&
                                _maps.TryGetMap(target.MapId, out var targetMap) &&
                                TryComp<KoronusPlanetSurfaceMapComponent>(targetMap.Value, out var surface) &&
                                surface.SystemId == target.SystemId;
        if (!isSystemMap && !isMatchingSurface)
        {
            reason = KoronusActivityRejectReason.MapMismatch;
            return false;
        }

        if (_safety.HasRule(target.MapId, target.Position, ProtectedAreaRules))
        {
            reason = KoronusActivityRejectReason.ProtectedArea;
            return false;
        }

        if (target.CandidateGrid is { Valid: true } grid &&
            _safety.HasRule(grid, ProtectedAreaRules))
        {
            reason = KoronusActivityRejectReason.ProtectedGrid;
            return false;
        }

        reason = KoronusActivityRejectReason.None;
        return true;
    }
}

public readonly record struct KoronusActivityTarget(
    string SystemId,
    MapId MapId,
    Vector2 Position,
    EntityUid? CandidateGrid = null,
    bool ShuttleInFtl = false,
    bool PlanetaryTransit = false);
