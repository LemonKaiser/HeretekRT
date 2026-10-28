using System.Numerics;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.SectorMap.Systems;

public sealed partial class KoronusSectorRuleSystem
{
    internal bool TryGetArrivalPosition(KoronusSystemPrototype system, MapId map, float radius, out Vector2 position)
    {
        position = default;
        var outerRadius = system.BoundaryRadius - radius - ObjectPlacementPadding;
        if (outerRadius <= radius + ObjectPlacementPadding)
            return false;

        var minDistance = MathF.Min(6000f, outerRadius / 2f);
        var maxDistance = MathF.Min(12000f, outerRadius);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var candidate = system.NavigationCenter + _random.NextAngle().ToWorldVec() *
                _random.NextFloat(minDistance, maxDistance);
            if (!IsInitialGridPositionClear(system, candidate, radius, includeRandomBodies: true))
                continue;

            var clear = true;
            foreach (var grid in _maps.GetAllGrids(map))
            {
                var transform = Transform(grid.Owner);
                var clearance = radius + GetGridHalfDiagonal(grid.Comp) +
                    grid.Comp.LocalAABB.Center.Length() + ObjectPlacementPadding;
                if (Vector2.DistanceSquared(candidate, _transform.GetWorldPosition(transform)) >= clearance * clearance)
                    continue;
                clear = false;
                break;
            }

            if (!clear)
                continue;
            position = candidate;
            return true;
        }

        return false;
    }
}
