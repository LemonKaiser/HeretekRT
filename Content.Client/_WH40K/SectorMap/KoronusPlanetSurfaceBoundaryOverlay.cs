using System.Numerics;
using Content.Shared._WH40K.SectorMap.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.SectorMap;

/// <summary>
/// Shows the outside of the square planetary play area as a local red danger band, matching the
/// sector boundary treatment without adding a warning or an extra marker to NAV.
/// </summary>
public sealed class KoronusPlanetSurfaceBoundaryOverlay : Overlay
{
    private const float WarningDistance = 3f;
    private const float BandMaxAlpha = 0.78f;
    private const float EdgeMaxAlpha = 0.96f;

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IMapManager _maps = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public KoronusPlanetSurfaceBoundaryOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var mapUid = _maps.GetMapEntityId(args.MapId);
        if (args.Viewport.Eye is not { } eye ||
            !_entities.TryGetComponent<KoronusPlanetSurfaceBoundaryComponent>(mapUid, out var boundary))
        {
            return false;
        }

        return GetWarningStrength(eye.Position.Position, boundary) > 0f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var mapUid = _maps.GetMapEntityId(args.MapId);
        if (!_entities.TryGetComponent<KoronusPlanetSurfaceBoundaryComponent>(mapUid, out var boundary))
            return;

        if (args.Viewport.Eye is not { } eye)
            return;

        var warningStrength = GetWarningStrength(eye.Position.Position, boundary);
        if (warningStrength <= 0f)
            return;

        var playableBounds = new Box2(boundary.Minimum, boundary.Maximum);
        var visibleBounds = args.WorldAABB;
        var handle = args.WorldHandle;
        var bandColor = Color.FromHex("#4A0606").WithAlpha(BandMaxAlpha * warningStrength);
        var edgeColor = Color.FromHex("#E02828").WithAlpha(EdgeMaxAlpha * warningStrength);

        DrawBand(handle, visibleBounds.Left, MathF.Min(visibleBounds.Right, playableBounds.Left), visibleBounds.Bottom, visibleBounds.Top, bandColor);
        DrawBand(handle, MathF.Max(visibleBounds.Left, playableBounds.Right), visibleBounds.Right, visibleBounds.Bottom, visibleBounds.Top, bandColor);
        DrawBand(handle,
            MathF.Max(visibleBounds.Left, playableBounds.Left),
            MathF.Min(visibleBounds.Right, playableBounds.Right),
            visibleBounds.Bottom,
            MathF.Min(visibleBounds.Top, playableBounds.Bottom),
            bandColor);
        DrawBand(handle,
            MathF.Max(visibleBounds.Left, playableBounds.Left),
            MathF.Min(visibleBounds.Right, playableBounds.Right),
            MathF.Max(visibleBounds.Bottom, playableBounds.Top),
            visibleBounds.Top,
            bandColor);
        handle.DrawRect(playableBounds, edgeColor, filled: false);
    }

    private static float GetWarningStrength(Vector2 position, KoronusPlanetSurfaceBoundaryComponent boundary)
    {
        var distanceToEdge = MathF.Min(
            MathF.Min(position.X - boundary.Minimum.X, boundary.Maximum.X - position.X),
            MathF.Min(position.Y - boundary.Minimum.Y, boundary.Maximum.Y - position.Y));

        return Math.Clamp(1f - distanceToEdge / WarningDistance, 0f, 1f);
    }

    private static void DrawBand(DrawingHandleWorld handle, float left, float right, float bottom, float top, Color color)
    {
        if (left >= right || bottom >= top)
            return;

        handle.DrawRect(new Box2(left, bottom, right, top), color);
    }
}
