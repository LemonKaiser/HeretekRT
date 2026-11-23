using System.Numerics;
using Content.Client.Resources;
using Content.Client.Viewport;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Roof;
using Content.Shared.Light.Components;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client._CE.ZLevels.Roof;

/// <summary>Draws authored roof masks as opaque covers when their grid is viewed from above.</summary>
public sealed partial class CEZRoofCoverOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IResourceCache _resources = default!;

    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _transform;
    private readonly Texture _roofTexture;
    private List<Entity<MapGridComponent>> _grids = [];

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public CEZRoofCoverOverlay()
    {
        IoCManager.InjectDependencies(this);
        _map = _entities.System<SharedMapSystem>();
        _transform = _entities.System<SharedTransformSystem>();
        _roofTexture = _resources.GetTexture("/Textures/Tiles/steel_maint.png");
        ZIndex = 1100;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return args.Viewport.Eye is ScalingViewport.ZEye { DrawRoofCover: true };
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _grids.Clear();
        _map.FindGridsIntersecting(args.MapId, args.WorldAABB, ref _grids, approx: true, includeMap: true);

        var handle = args.WorldHandle;
        foreach (var grid in _grids)
        {
            // These masks describe real tiles already drawn on higher levels.
            if (_entities.HasComponent<CEZLevelRoofComponent>(grid.Owner) ||
                _entities.HasComponent<CEZGridComponent>(grid.Owner) ||
                !_entities.TryGetComponent<RoofComponent>(grid.Owner, out var roof) || roof.Data.Count == 0)
            {
                continue;
            }

            var (_, _, matrix, inverse) = _transform.GetWorldPositionRotationMatrixWithInv(grid.Owner);
            var localBounds = inverse.TransformBox(args.WorldBounds);
            var size = grid.Comp.TileSize;
            var chunkSize = RoofComponent.ChunkSize;
            var chunkWidth = chunkSize * size;
            var firstX = (int) MathF.Floor(localBounds.Left / chunkWidth);
            var firstY = (int) MathF.Floor(localBounds.Bottom / chunkWidth);
            var lastX = (int) MathF.Floor(localBounds.Right / chunkWidth);
            var lastY = (int) MathF.Floor(localBounds.Top / chunkWidth);

            handle.SetTransform(matrix);
            for (var y = firstY; y <= lastY; y++)
            {
                for (var x = firstX; x <= lastX; x++)
                {
                    if (!roof.Data.TryGetValue(new Vector2i(x, y), out var mask))
                        continue;

                    while (mask != 0)
                    {
                        var bit = BitOperations.TrailingZeroCount(mask);
                        mask &= mask - 1;
                        var left = x * chunkWidth + bit % chunkSize * size;
                        var bottom = y * chunkWidth + bit / chunkSize * size;
                        var tileBounds = new Box2(left, bottom, left + size, bottom + size);
                        handle.DrawTextureRect(_roofTexture, tileBounds);
                    }
                }
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
