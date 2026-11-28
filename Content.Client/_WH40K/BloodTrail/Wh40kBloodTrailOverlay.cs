using System.Numerics;
using Content.Client.Viewport;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.BloodTrail;

/// <summary>
/// Batched viewport-culled overlay drawing client-side blood footprints and drag marks under entities.
/// </summary>
public sealed partial class Wh40kBloodTrailOverlay : Overlay
{
    [Dependency] private IEntityManager _entMan = default!;

    private static readonly ResPath Rsi = new("/Textures/_WH40K/BloodTrail/footprints.rsi");

    private readonly Wh40kBloodTrailSystem _trails;
    private readonly SpriteSystem _sprite;
    private readonly SharedTransformSystem _transform;
    private readonly Texture[] _textures;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public Wh40kBloodTrailOverlay(Wh40kBloodTrailSystem trails)
    {
        IoCManager.InjectDependencies(this);
        _trails = trails;
        _sprite = _entMan.System<SpriteSystem>();
        _transform = _entMan.System<SharedTransformSystem>();
        // Just above snow prints (12), below puddles/mobs/items.
        ZIndex = 13;
        _textures = new[]
        {
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "footprint-left-bare-human")),
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "footprint-right-bare-human")),
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "footprint-shoes")),
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "footprint-suit")),
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "dragging-1")),
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "dragging-2")),
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "dragging-3")),
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "dragging-4")),
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "dragging-5")),
        };
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _trails.PrintCount > 0
               && (args.Viewport.Eye is not ScalingViewport.ZEye zEye || MathF.Abs(zEye.Depth) <= 0.001f);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        foreach (var (grid, tiles) in _trails.Prints)
        {
            if (tiles.Count == 0 || !_entMan.TryGetComponent(grid, out TransformComponent? xform) || xform.MapID != args.MapId)
                continue;

            var (_, _, matrix, inverse) = _transform.GetWorldPositionRotationMatrixWithInv(grid);
            var bounds = inverse.TransformBox(args.WorldAABB).Enlarged(1f);
            var minX = (int) MathF.Floor(bounds.Left);
            var maxX = (int) MathF.Ceiling(bounds.Right);
            var minY = (int) MathF.Floor(bounds.Bottom);
            var maxY = (int) MathF.Ceiling(bounds.Top);
            var viewTiles = (long) (maxX - minX + 1) * (maxY - minY + 1);

            handle.SetTransform(matrix);

            if (tiles.Count > viewTiles && viewTiles is > 0 and < 4096)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    for (var y = minY; y <= maxY; y++)
                    {
                        if (!tiles.TryGetValue(new Vector2i(x, y), out var list))
                            continue;

                        DrawTileList(handle, list);
                    }
                }
            }
            else
            {
                foreach (var (tile, list) in tiles)
                {
                    if (tile.X < minX || tile.X > maxX || tile.Y < minY || tile.Y > maxY)
                        continue;

                    DrawTileList(handle, list);
                }
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawTileList(DrawingHandleWorld handle, List<Wh40kBloodPrint> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var print = list[i];
            var alpha = _trails.Visibility(print);
            if (alpha <= 0.01f)
                continue;

            var box = Box2.CenteredAround(print.Position, Vector2.One);
            handle.DrawTextureRect(
                _textures[(int) print.Kind],
                new Box2Rotated(box, print.Rotation, print.Position),
                print.Color.WithAlpha(print.Color.A * alpha));
        }
    }
}
