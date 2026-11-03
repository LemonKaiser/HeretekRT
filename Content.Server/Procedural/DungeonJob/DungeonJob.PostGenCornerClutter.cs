using System.Threading.Tasks;
using Content.Shared.Procedural;
using Content.Shared.Procedural.PostGeneration;
using Content.Shared.Storage;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// <see cref="CornerClutterDunGen"/>
    /// </summary>
    private async Task PostGen(CornerClutterDunGen gen, DungeonData data, Dungeon dungeon, HashSet<Vector2i> reservedTiles, Random random)
    {
        if (!data.SpawnGroups.TryGetValue(DungeonDataKey.CornerClutter, out var corner))
        {
            _sawmill.Error(Environment.StackTrace);
            return;
        }

        foreach (var tile in dungeon.CorridorTiles)
        {
            if (reservedTiles.Contains(tile))
                continue;

            if (!_anchorable.TileFree((_gridUid, _grid), tile, DungeonSystem.CollisionLayer, DungeonSystem.CollisionMask))
                continue;

            if (gen.PreservePassages)
            {
                var nearEntrance = dungeon.Entrances.Contains(tile);
                for (var i = 0; i < 4 && !nearEntrance; i++)
                    nearEntrance = dungeon.Entrances.Contains(tile + ((Direction) (i * 2)).ToIntVec());

                if (nearEntrance)
                    continue;
            }

            // If at least 2 adjacent tiles are blocked consider it a corner
            for (var i = 0; i < 4; i++)
            {
                var dir = (Direction) (i * 2);
                var blocked = HasWall(tile + dir.ToIntVec());

                if (!blocked)
                    continue;

                var nextDir = (Direction) ((i + 1) * 2 % 8);
                blocked = HasWall(tile + nextDir.ToIntVec());

                if (!blocked)
                    continue;

                if (gen.PreservePassages)
                {
                    var away = -dir.ToIntVec();
                    var nextAway = -nextDir.ToIntVec();
                    if (!IsFreeCorridorCorner(tile + away) || !IsFreeCorridorCorner(tile + nextAway) ||
                        !IsFreeCorridorCorner(tile + away + nextAway))
                        continue;
                }

                if (random.Prob(gen.Chance))
                {
                    var coords = _maps.GridTileToLocal(_gridUid, _grid, tile);
                    var protos = EntitySpawnCollection.GetSpawns(_prototype.Index(corner).Entries, random);
                    _entManager.SpawnEntities(coords, protos);
                }

                break;
            }
        }

        bool IsFreeCorridorCorner(Vector2i tile)
        {
            return dungeon.CorridorTiles.Contains(tile) && !reservedTiles.Contains(tile) &&
                   _anchorable.TileFree((_gridUid, _grid), tile, DungeonSystem.CollisionLayer, DungeonSystem.CollisionMask);
        }
    }
}
