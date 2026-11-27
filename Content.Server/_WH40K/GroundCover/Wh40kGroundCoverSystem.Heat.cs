using Content.Server.Atmos.Components;
using Content.Shared._WH40K.GroundCover;

namespace Content.Server._WH40K.GroundCover;

public sealed partial class Wh40kGroundCoverSystem
{
    private static readonly TimeSpan HeatInterval = TimeSpan.FromSeconds(2);

    private const float HotspotMeltLevelsPerMinute = 12f;

    private TimeSpan _nextHeat;

    /// <summary>Fire melts snow on its tile and the surrounding tiles.</summary>
    private void UpdateHeat(TimeSpan now)
    {
        if (now < _nextHeat)
            return;

        _nextHeat = now + HeatInterval;
        var perPass = HotspotMeltLevelsPerMinute * (float) HeatInterval.TotalMinutes * Wh40kSnowCover.UnitsPerLevel;
        var query = EntityQueryEnumerator<GridAtmosphereComponent, Wh40kSnowCoverComponent>();
        while (query.MoveNext(out var grid, out var atmosphere, out _))
        {
            foreach (var tile in atmosphere.HotspotTiles)
            {
                if (!tile.Hotspot.Valid)
                    continue;

                for (var x = -1; x <= 1; x++)
                {
                    for (var y = -1; y <= 1; y++)
                    {
                        var index = tile.GridIndices + new Vector2i(x, y);
                        var depth = GetDepth(grid, index);
                        if (depth > 0)
                            SetDepth(grid, index, depth - (int) MathF.Ceiling(x == 0 && y == 0 ? perPass : perPass * 0.5f));
                    }
                }
            }
        }
    }
}
