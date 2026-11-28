using System.Linq;
using System.Numerics;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Client.GameObjects;
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
        if (mapId == MapId.Nullspace || !_map.MapExists(mapId))
            return false;

        Entity<MapGridComponent>? grid;
        void OnEntityInitialized(Entity<MetaDataComponent> entity) => RestorePreviewSprite(entity, mapId);
        EntityManager.EntityInitialized += OnEntityInitialized;
        try
        {
            if (!_loader.TryLoadGrid(mapId, vessel.ShuttlePath, out grid))
                return false;
        }
        finally
        {
            EntityManager.EntityInitialized -= OnEntityInitialized;
        }

        Dispose();
        _xform.SetMapCoordinates(grid.Value, new MapCoordinates(Vector2.Zero, mapId));
        _meta.SetEntityName(grid.Value, vessel.Name);
        CurrentGrid = grid.Value;
        return true;
    }

    private void RestorePreviewSprite(Entity<MetaDataComponent> entity, MapId mapId)
    {
        if (!EntityManager.IsClientSide(entity, entity.Comp) || Transform(entity).MapID != mapId ||
            !TryComp<SpriteComponent>(entity, out var sprite) || (sprite.LayerData.Count != 0 && sprite.BaseRSI != null))
            return;

        // We have to do this or else Robust explodes on newer engine versions.

        // Long story short, on new engine versions, spriteloading work got moved to ComponentAdd,
        // when before there was a deserialization hook.
        // The map deserializer also adds an empty Sprite component that causes ComponentAdd to initialize
        // the entire sprite BEFORE the actual one gets added for some reason,
        // so we have to re-copy the sprite so everything shows up correct.
        // A visualizer can add a layer during ComponentInit even though the base RSI is still missing.

        // I hate RobustToolbox.
        EntityManager.CopyComponent(entity, entity, sprite, entity.Comp);
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
