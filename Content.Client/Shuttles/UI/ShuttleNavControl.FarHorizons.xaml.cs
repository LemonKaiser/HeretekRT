using System.Numerics;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private readonly List<BlipData> _beaconBlips = new();

    private void DrawStarSystem(DrawingHandleScreen handle, Matrix3x2 worldToShuttle, Matrix3x2 shuttleToView, EntityUid? mapUid)
    {
        if (!EntManager.TryGetComponent<StarSystemMapComponent>(mapUid, out var starSystem) ||
            starSystem.StarSystem == null)
            return;

        var worldToView = worldToShuttle * shuttleToView;
        var viewScale = MathF.Sqrt((worldToView.M11 * worldToView.M11) + (worldToView.M12 * worldToView.M12));

        var starPos = Vector2.Transform(starSystem.StarSystem.Star.Position, worldToView);
        var starRadius = Star.NAV_PIXEL_SIZE * starSystem.StarSystem.Star.Radius * viewScale;

        handle.DrawCircle(starPos, starRadius, starSystem.StarSystem.Star.Color.WithAlpha(0.5f));

        foreach (var planet in starSystem.StarSystem.Planets)
        {
            var planetPos = Vector2.Transform(planet.Position, worldToView);
            var planetRadius = Planet.NAV_PIXEL_SIZE * planet.Radius * viewScale;
            handle.DrawCircle(planetPos, planetRadius, Color.Gray.WithAlpha(0.5f));
        }
    }

    // code stolen from IFF beacon rendering code for grids
    private void DrawIFFBeacons(DrawingHandleScreen handle, Matrix3x2 worldToView, MapCoordinates mapPos, EntityUid? mapUid)
    {
        if (!ShowIFF || mapUid == null)
            return;

        _beaconBlips.Clear();

        var uiXCentre = (int)Width / 2;
        var uiYCentre = (int)Height / 2;
        var blipSize = RadarBlipSize * 0.7f;

        var query = EntManager.AllEntityQueryEnumerator<IFFComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var iff, out var xform))
        {
            if (xform.MapUid != mapUid ||
                EntManager.HasComponent<MapGridComponent>(uid) ||
                (iff.Flags & IFFFlags.Hide) != 0x0)
                continue;

            if (_shuttles.GetIFFLabel(uid, component: iff) is not { } label)
                continue;

            var color = _shuttles.GetIFFColor(uid, self: false, iff);
            var worldPos = _transform.GetWorldPosition(uid);
            var uiPosition = Vector2.Transform(worldPos, worldToView) / UIScale;

            var uiXOffset = uiPosition.X - uiXCentre;
            var uiYOffset = uiPosition.Y - uiYCentre;
            var uiDistance = MathF.Sqrt(uiXOffset * uiXOffset + uiYOffset * uiYOffset);
            if (uiDistance <= float.Epsilon)
                uiDistance = 1f;

            var isOutsideRadarCircle = !Wh40kOnScope(uiPosition * UIScale);
            if (isOutsideRadarCircle)
            {
                uiPosition = new Vector2(
                    x: uiXCentre * uiXOffset / uiDistance * 0.95f + uiXCentre,
                    y: uiYCentre * uiYOffset / uiDistance * 0.95f + uiYCentre
                );
            }

            NfAddBlipToList(_beaconBlips, isOutsideRadarCircle, uiPosition, uiXCentre, uiYCentre, color);

            if (isOutsideRadarCircle)
                continue;

            var distance = Vector2.Distance(worldPos, mapPos.Position);
            var displayedDistance = distance < 50f ? $"{distance:0.0}" : distance < 1000 ? $"{distance:0}" : $"{distance / 1000:0.0}k";

            var lines = Loc.GetString("shuttle-console-iff-label", ("name", label), ("distance", displayedDistance)).Split('\n');
            var mainLabel = lines[0];

            var labelOffset = new Vector2(blipSize, -handle.GetDimensions(Font, mainLabel, 0.9f).Y * 0.5f);
            Wh40kBeginTag(uiPosition * UIScale, (uiPosition + labelOffset) * UIScale, always: true, key: uid.Id);
            Wh40kTagLine(handle, mainLabel, UIScale * 0.9f, color);

            if (!ShowIFFDetailed)
                continue;

            if (lines.Length > 1)
                Wh40kTagLine(handle, lines[1], UIScale * 0.7f, color);

            var coordsText = $"({worldPos.X:0.0}, {worldPos.Y:0.0})";
            Wh40kTagLine(handle, coordsText, UIScale * 0.7f, color);
        }

        NfDrawBlips(handle, _beaconBlips);
    }
}
