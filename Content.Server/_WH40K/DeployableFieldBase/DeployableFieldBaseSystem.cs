using System.Numerics;
using System.Linq;
using Content.Server._WH40K.SectorMap.Components;
using Content.Shared._WH40K.DeployableFieldBase;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WH40K.DeployableFieldBase;

/// <summary>
/// Builds a small self-contained grid. It intentionally avoids runtime map loading on a live
/// planetary surface, which would otherwise cause a hitch when the grid preloader has no spare copy.
/// </summary>
public sealed partial class DeployableFieldBaseSystem : SharedDeployableFieldBaseSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DeployableFieldBaseComponent, DeployableFieldBaseDoAfterEvent>(OnDeploy);
    }

    private void OnDeploy(Entity<DeployableFieldBaseComponent> ent, ref DeployableFieldBaseDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !CanDeploy(ent))
            return;

        Deploy(ent);
        args.Handled = true;
        QueueDel(ent);
    }

    private bool CanDeploy(Entity<DeployableFieldBaseComponent> ent)
    {
        var transform = Transform(ent);
        if (transform.MapID == MapId.Nullspace)
            return false;

        var map = _maps.GetMapOrInvalid(transform.MapID);
        if (!TryComp<KoronusPlanetSurfaceMapComponent>(map, out var surface) ||
            transform.GridUid != surface.TerrainGrid ||
            !TryComp<MapGridComponent>(surface.TerrainGrid, out var terrain))
        {
            Popup(ent, "field-base-fail-no-planet");
            return false;
        }

        var mapCoordinates = _transform.ToMapCoordinates(transform.Coordinates);
        if (!surface.PlayableBounds.Contains(mapCoordinates.Position))
        {
            Popup(ent, "field-base-fail-no-space");
            return false;
        }

        var radius = MathF.Max(ent.Comp.Size.X, ent.Comp.Size.Y) + 2f;
        if (_lookup.GetEntitiesInRange<MapGridComponent>(mapCoordinates, radius).Any(grid => grid.Owner != surface.TerrainGrid))
        {
            Popup(ent, "field-base-fail-near-grid");
            return false;
        }

        var local = _transform.ToCoordinates(surface.TerrainGrid, mapCoordinates);
        var size = new Vector2(ent.Comp.Size.X + 2f, ent.Comp.Size.Y + 2f);
        var box = Box2.CenteredAround(local.Position, size);
        if (_maps.GetAnchoredEntities(surface.TerrainGrid, terrain, box).Any())
        {
            Popup(ent, "field-base-fail-no-space");
            return false;
        }

        return true;
    }

    private void Deploy(Entity<DeployableFieldBaseComponent> ent)
    {
        var transform = Transform(ent);
        var mapCoordinates = _transform.ToMapCoordinates(transform.Coordinates);
        var grid = _maps.CreateGridEntity(transform.MapID);
        var origin = mapCoordinates.Position - new Vector2(ent.Comp.Size.X / 2f, ent.Comp.Size.Y / 2f);
        _transform.SetMapCoordinates(grid, new MapCoordinates(origin, transform.MapID));

        var floor = _tiles["FloorSteel"].TileId;
        for (var x = 0; x < ent.Comp.Size.X; x++)
        {
            for (var y = 0; y < ent.Comp.Size.Y; y++)
            {
                _maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), new Tile(floor));
                var isBorder = x == 0 || x == ent.Comp.Size.X - 1 || y == 0 || y == ent.Comp.Size.Y - 1;
                if (!isBorder)
                    continue;

                var prototype = x == ent.Comp.Size.X / 2 && y == 0 ? "Airlock" : "WallSolid";
                Spawn(prototype, new EntityCoordinates(grid.Owner, new Vector2(x + 0.5f, y + 0.5f)));
            }
        }
    }

    private void Popup(EntityUid uid, LocId message)
    {
        var coordinates = Transform(uid).Coordinates;
        EntityManager.System<Content.Shared.Popups.SharedPopupSystem>().PopupCoordinates(Loc.GetString(message), coordinates);
    }
}
