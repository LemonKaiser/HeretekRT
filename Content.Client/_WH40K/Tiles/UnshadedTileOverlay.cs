using System.Numerics;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Tiles;

/// <summary>
/// Draws tile types that must not be affected by lighting.
/// </summary>
public sealed partial class UnshadedTileOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitions = default!;

    private readonly EntityLookupSystem _lookup;
    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _transformSystem;
    private readonly ShaderInstance _unshadedShader;
    private List<Entity<MapGridComponent>> _grids = [];

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public UnshadedTileOverlay(IEntityManager entityManager)
    {
        IoCManager.InjectDependencies(this);

        _lookup = entityManager.System<EntityLookupSystem>();
        _mapSystem = entityManager.System<SharedMapSystem>();
        _transformSystem = entityManager.System<SharedTransformSystem>();
        _unshadedShader = _prototypes.Index(UnshadedShader).Instance();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        _grids.Clear();
        _mapSystem.FindGridsIntersecting(args.MapId, args.WorldBounds, ref _grids, approx: true);

        var worldHandle = args.WorldHandle;
        worldHandle.UseShader(_unshadedShader);

        foreach (var grid in _grids)
        {
            worldHandle.SetTransform(_transformSystem.GetWorldMatrix(grid.Owner));

            var tiles = _mapSystem.GetTilesEnumerator(grid.Owner, grid, args.WorldBounds);
            while (tiles.MoveNext(out var tileRef))
            {
                if (_tileDefinitions[tileRef.Tile.TypeId] is not ContentTileDefinition { Unshaded: true })
                    continue;

                worldHandle.DrawRect(_lookup.GetLocalBounds(tileRef, grid.Comp.TileSize), Color.White);
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
