using System.Linq;
using System.Numerics;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Client.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Client._Mono.Shipyard;

/// <summary>
/// This handles spawning client-side grid and getting data from it.
/// </summary>
public sealed class ShipyardPreviewSystem : SharedShipyardPreviewSystem
{
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private TransformSystem _xform = default!;

    public Entity<MapGridComponent>? CurrentGrid;
    public bool TryPreviewGrid(VesselPrototype vessel, MapId mapId)
    {
        Dispose();
        if (mapId == MapId.Nullspace || !_map.MapExists(mapId))
            return false;

        var opts = new DeserializationOptions();
        if (!_loader.TryLoadGrid(mapId,
                vessel.ShuttlePath,
                out var grid,
                opts))
            return false;

        _xform.SetMapCoordinates(grid.Value, new MapCoordinates(Vector2.Zero, mapId));
        _meta.SetEntityName(grid.Value, vessel.Name);
        CurrentGrid = grid.Value;
        return true;
    }

    public FormattedMessage GetGridData()
    {
        var msg = new FormattedMessage();
        if (CurrentGrid == null)
            return msg;

        msg.AddMarkupOrThrow(
            Loc.GetString("shipyard-preview-tile-count", ("count", _map.GetAllTiles(CurrentGrid.Value.Owner, CurrentGrid.Value.Comp).Count().ToString()))
            );

        return msg;
    }

    public void Dispose()
    {
        Del(CurrentGrid);
        CurrentGrid = null;
    }
}

