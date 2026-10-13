using System.Numerics;
using Content.Shared._WH40K.SectorMap.BUI;
using Content.Shared.Shuttles.Systems;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private static readonly Color MiningFieldColor = Color.FromHex("#999999");
    private readonly List<(KoronusAsteroidClusterState Field, string Label)> _asteroidFields = new();
    private readonly List<(Vector2 ScreenPosition, Vector2 WorldPosition)> _asteroidFieldTargets = new();
    private MapId _asteroidFieldMap;
    private Matrix3x2 _asteroidFieldMatrix;

    private void UpdateAsteroidFields(List<KoronusAsteroidClusterState> fields)
    {
        _asteroidFields.Clear();
        _asteroidFieldTargets.Clear();
        foreach (var field in fields)
            _asteroidFields.Add((field, Loc.GetString("koronus-asteroid-field-name", ("number", field.Number))));
    }

    private void DrawAsteroidFields(DrawingHandleScreen handle, MapId map, Matrix3x2 worldToView, Vector2 observer)
    {
        _asteroidFieldTargets.Clear();
        _asteroidFieldMap = map;
        _asteroidFieldMatrix = worldToView;
        if (_ftlState is FTLState.Starting or FTLState.Travelling or FTLState.Arriving ||
            _planetaryState.NavigationSuppressed)
            return;

        var margin = 20f * UIScale;
        if (PixelSize.X <= margin * 2 || PixelSize.Y <= margin * 2)
            return;
        foreach (var (field, label) in _asteroidFields)
        {
            var center = Vector2.Transform(field.Position, worldToView);
            var radius = field.Radius * MinimapScale;
            // The charted boundary remains visible even though the individual grids are outside radar range.
            if (center.X + radius >= 0 && center.Y + radius >= 0 &&
                center.X - radius <= PixelSize.X && center.Y - radius <= PixelSize.Y)
            {
                handle.DrawCircle(center, radius, MiningFieldColor.WithAlpha(0.65f), filled: false);
            }

            // Labels stay at their charted location; distant areas never become edge arrows.
            if (center.X < margin || center.Y < margin ||
                center.X > PixelSize.X - margin || center.Y > PixelSize.Y - margin)
                continue;

            _asteroidFieldTargets.Add((center, field.Position));
            var distance = Vector2.Distance(observer, field.Position);
            var text = Loc.GetString("koronus-asteroid-field-nav", ("name", label), ("distance", (int) distance));
            var size = handle.GetDimensions(Font, text, 0.65f * UIScale);
            var textPosition = center + new Vector2(-size.X / 2f, 16f * UIScale);
            textPosition.X = Math.Clamp(textPosition.X, 4f, MathF.Max(4f, PixelSize.X - size.X - 4f));
            textPosition.Y = Math.Clamp(textPosition.Y, size.Y, MathF.Max(size.Y, PixelSize.Y - 4f));
            handle.DrawString(Font, textPosition, text, 0.65f * UIScale,
                MiningFieldColor.WithAlpha(distance < field.Radius ? 0.6f : 0.85f));
        }
    }

    public bool TryGetAsteroidFieldTarget(EntityCoordinates coordinates, out EntityCoordinates target)
    {
        target = default;
        var point = _transform.ToMapCoordinates(coordinates);
        if (point.MapId != _asteroidFieldMap || MinimapScale <= 0f)
            return false;
        var screen = Vector2.Transform(point.Position, _asteroidFieldMatrix);
        foreach (var marker in _asteroidFieldTargets)
        {
            if (Vector2.DistanceSquared(marker.ScreenPosition, screen) > 18f * 18f * UIScale * UIScale)
                continue;
            target = _transform.ToCoordinates(new MapCoordinates(marker.WorldPosition, point.MapId));
            return true;
        }
        return false;
    }
}
