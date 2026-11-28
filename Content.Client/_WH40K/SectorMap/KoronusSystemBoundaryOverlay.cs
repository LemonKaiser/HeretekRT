using System.Numerics;
using Content.Shared._WH40K.SectorMap;
using Content.Shared._WH40K.SectorMap.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WH40K.SectorMap;

/// <summary>
/// Draws the shared red danger area outside the system edge when the viewer enters its warning area.
/// </summary>
public sealed class KoronusSystemBoundaryOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    private readonly Vector2[] _outlineVertices = new Vector2[KoronusSystemBoundaryRenderer.OutlineVertexCount];
    private readonly Vector2[] _dangerBandVertices = new Vector2[KoronusSystemBoundaryRenderer.DangerBandVertexCount];

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public KoronusSystemBoundaryOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not { } eye ||
            !_entities.TryGetComponent<KoronusSystemBoundaryComponent>(args.MapUid, out var boundary))
        {
            return false;
        }

        return KoronusSystemBoundaryMath.IsInWarningArea(
            eye.Position.Position,
            boundary.Origin,
            boundary.Radius,
            boundary.WarningFraction);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_entities.TryGetComponent<KoronusSystemBoundaryComponent>(args.MapUid, out var boundary))
            return;

        var outerRadius = MathF.Max(
                              MathF.Max(
                                  Vector2.Distance(boundary.Origin, args.WorldAABB.BottomLeft),
                                  Vector2.Distance(boundary.Origin, args.WorldAABB.BottomRight)),
                              MathF.Max(
                                  Vector2.Distance(boundary.Origin, args.WorldAABB.TopLeft),
                                  Vector2.Distance(boundary.Origin, args.WorldAABB.TopRight))) + 1f;

        KoronusSystemBoundaryRenderer.DrawDangerBand(
            args.WorldHandle,
            boundary.Origin,
            boundary.Radius,
            outerRadius,
            _dangerBandVertices);
        KoronusSystemBoundaryRenderer.DrawOutline(
            args.WorldHandle,
            boundary.Origin,
            boundary.Radius,
            _outlineVertices);
    }
}
