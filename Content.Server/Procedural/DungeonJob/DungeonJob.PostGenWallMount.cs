using System.Threading.Tasks;
using Content.Shared.Procedural;
using Content.Shared.Procedural.PostGeneration;
using Content.Shared.Storage;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// <see cref="WallMountDunGen"/>
    /// </summary>
    private async Task PostGen(WallMountDunGen gen, DungeonData data, Dungeon dungeon, HashSet<Vector2i> reservedTiles, Random random)
    {
        if (!data.Tiles.TryGetValue(DungeonDataKey.FallbackTile, out var tileProto))
        {
            _sawmill.Error($"Tried to run {nameof(WallMountDunGen)} without any dungeon data set which is unsupported");
            return;
        }

        var tileDef = _prototype.Index(tileProto);
        bool validProto = data.SpawnGroups.TryGetValue(DungeonDataKey.WallMounts, out var spawnProto); // Frontier: assign to validProto
        if (!validProto) // Frontier: add error handling
            _sawmill.Warning($"No wallmount spawn group for dungeon type."); // Frontier: add error handling

        var checkedTiles = new HashSet<Vector2i>();
        var allExterior = new HashSet<Vector2i>(dungeon.CorridorExteriorTiles);
        allExterior.UnionWith(dungeon.RoomExteriorTiles);
        var mounts = new List<(Vector2i Position, Vector2i Direction)>();
        var count = 0;

        foreach (var neighbor in allExterior)
        {
            // Occupado
            if (dungeon.RoomTiles.Contains(neighbor) || checkedTiles.Contains(neighbor))
                continue;

            var position = neighbor;
            var direction = Vector2i.Zero;
            var rotation = Angle.Zero;

            if (gen.MountInside)
            {
                if (!TryGetInteriorWallMount(dungeon, neighbor, reservedTiles, out position, out direction))
                    continue;

                var tooClose = false;
                foreach (var mount in mounts)
                {
                    if (mount.Direction != direction)
                        continue;

                    var delta = mount.Position - position;
                    if (delta.X * delta.X + delta.Y * delta.Y >= gen.MinSpacing * gen.MinSpacing)
                        continue;

                    tooClose = true;
                    break;
                }

                if (tooClose)
                    continue;

                // The fixture's unrotated sprite offset points north, towards its wall.
                rotation = new Angle(Math.Atan2(-direction.X, direction.Y));
            }
            else if (!_anchorable.TileFree(_grid, neighbor, DungeonSystem.CollisionLayer, DungeonSystem.CollisionMask))
            {
                continue;
            }

            if (!random.Prob(gen.Prob) || !checkedTiles.Add(neighbor))
                continue;

            if (!gen.MountInside)
                _maps.SetTile(_gridUid, _grid, neighbor, _tile.GetVariantTile(tileDef, random));

            var gridPos = _maps.GridTileToLocal(_gridUid, _grid, position);

            if (!validProto) // Frontier: error handling
                continue; // Frontier: error handling
            var protoNames = EntitySpawnCollection.GetSpawns(_prototype.Index(spawnProto).Entries, random);

            if (gen.MountInside)
            {
                foreach (var proto in protoNames)
                {
                    if (proto == null)
                        continue;

                    var entity = _entManager.SpawnEntity(proto, gridPos);
                    _transform.SetLocalRotation(entity, rotation);
                }

                mounts.Add((position, direction));
            }
            else
            {
                _entManager.SpawnEntities(gridPos, protoNames);
            }
            count += protoNames.Count;

            if (count > 20)
            {
                count -= 20;
                await SuspendDungeon();

                if (!ValidateResume())
                    return;
            }
        }
    }

    private bool TryGetInteriorWallMount(
        Dungeon dungeon,
        Vector2i wall,
        HashSet<Vector2i> reservedTiles,
        out Vector2i position,
        out Vector2i direction)
    {
        position = default;
        direction = default;
        if (!HasWall(wall))
            return false;

        var adjacentFloors = 0;
        for (var i = 0; i < 4; i++)
        {
            var offset = ((Direction) (i * 2)).ToIntVec();
            var inside = wall + offset;
            if (!dungeon.RoomTiles.Contains(inside) && !dungeon.CorridorTiles.Contains(inside))
                continue;

            adjacentFloors++;
            position = inside;
            direction = -offset;
        }

        if (adjacentFloors != 1 || reservedTiles.Contains(position) || dungeon.Entrances.Contains(position) ||
            !_anchorable.TileFree(_grid, position, DungeonSystem.CollisionLayer, DungeonSystem.CollisionMask))
            return false;

        // Keep fixtures off corners and away from the ends of door openings.
        var tangent = new Vector2i(direction.Y, -direction.X);
        return HasWall(wall + tangent) && HasWall(wall - tangent);
    }
}
