using System.Numerics;
using Content.Client.Viewport;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.GroundCover;

/// <summary>Draws footprints over the snow cover, under entities.</summary>
public sealed partial class Wh40kSnowPrintsOverlay : Overlay
{
    [Dependency] private IEntityManager _entMan = default!;

    private static readonly ResPath Rsi = new("/Textures/_WH40K/GroundCover/footprints.rsi");

    /// <summary>A trodden-in blue grey, so prints read as hollows in the snow.</summary>
    private static readonly Color Tint = new(0.48f, 0.55f, 0.68f, 0.55f);

    private readonly Wh40kSnowPrintsSystem _prints;
    private readonly Wh40kGroundCoverSystem _cover;
    private readonly SpriteSystem _sprite;
    private readonly SharedTransformSystem _transform;
    private readonly Texture[] _textures;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public Wh40kSnowPrintsOverlay(Wh40kSnowPrintsSystem prints)
    {
        IoCManager.InjectDependencies(this);
        _prints = prints;
        _cover = _entMan.System<Wh40kGroundCoverSystem>();
        _sprite = _entMan.System<SpriteSystem>();
        _transform = _entMan.System<SharedTransformSystem>();
        // Over the snow cover the ground cover overlay draws at 10 and the stains at 11.
        ZIndex = 12;
        _textures = new[]
        {
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "footprint-left-bare-human")),
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "footprint-right-bare-human")),
            _sprite.Frame0(new SpriteSpecifier.Rsi(Rsi, "footprint-shoes")),
        };
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _prints.PrintCount > 0
               && (args.Viewport.Eye is not ScalingViewport.ZEye zEye || MathF.Abs(zEye.Depth) <= 0.001f);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var coverPrepared = false;
        foreach (var (grid, tiles) in _prints.Prints)
        {
            if (!_entMan.TryGetComponent(grid, out TransformComponent? xform) || xform.MapID != args.MapId)
                continue;

            if (!coverPrepared)
            {
                _cover.PrepareVesselCover(args.MapId, args.WorldAABB.Enlarged(2f));
                coverPrepared = true;
            }

            var (_, _, matrix, inverse) = _transform.GetWorldPositionRotationMatrixWithInv(grid);
            var bounds = inverse.TransformBox(args.WorldAABB).Enlarged(1f);
            handle.SetTransform(matrix);
            foreach (var (tile, list) in tiles)
            {
                if (!bounds.Contains(tile + new Vector2(0.5f, 0.5f))
                    || _cover.IsUnderVessel(args.MapId, Vector2.Transform(tile + new Vector2(0.5f, 0.5f), matrix)))
                    continue;

                foreach (var print in list)
                {
                    var alpha = _prints.Visibility(print);
                    if (alpha <= 0f)
                        continue;

                    var box = Box2.CenteredAround(print.Position, Vector2.One);
                    handle.DrawTextureRect(_textures[(int) print.Kind], new Box2Rotated(box, print.Rotation, print.Position),
                        Tint.WithAlpha(Tint.A * alpha));
                }
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
