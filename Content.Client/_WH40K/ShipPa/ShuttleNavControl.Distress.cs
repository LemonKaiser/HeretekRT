using System.Numerics;
using Content.Shared._WH40K.ShipPa;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private void DrawWh40kDistressSignals(DrawingHandleScreen handle, Matrix3x2 worldToView, MapId mapId)
    {
        var pulse = 0.5f + 0.5f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 5f);
        var query = EntManager.AllEntityQueryEnumerator<Wh40kShipAlertComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var alert, out var xform))
        {
            if (!alert.Distress || xform.MapID != mapId)
                continue;

            var centre = EntManager.TryGetComponent<PhysicsComponent>(uid, out var body) ? body.LocalCenter : Vector2.Zero;
            var worldPos = _transform.ToMapCoordinates(new EntityCoordinates(uid, centre)).Position;
            var view = Vector2.Transform(worldPos, worldToView);

            var inset = 12f * UIScale;
            var onScope = Wh40kOnScope(view);
            view = Vector2.Clamp(
                view,
                new Vector2(inset),
                Vector2.Max(new Vector2(inset), new Vector2(PixelSize.X, PixelSize.Y) - new Vector2(inset)));

            var color = alert.DistressColor;
            var ringRadius = (9f + 7f * pulse) * UIScale;
            handle.DrawCircle(view, ringRadius, color.WithAlpha(0.75f * (1f - 0.5f * pulse)), filled: false);
            handle.DrawCircle(view, 4f * UIScale, color, filled: true);

            if (!onScope)
                continue;

            var shipName = EntManager.TryGetComponent<MetaDataComponent>(uid, out var meta) && !string.IsNullOrWhiteSpace(meta.EntityName)
                ? meta.EntityName
                : Loc.GetString("wh40k-ship-pa-unknown-ship");
            var label = Loc.GetString("wh40k-ship-distress-marker", ("ship", shipName));
            var dimensions = handle.GetDimensions(Font, label, UIScale * 0.85f);
            var labelX = view.X + 12f * UIScale + dimensions.X > PixelSize.X
                ? view.X - 12f * UIScale - dimensions.X
                : view.X + 12f * UIScale;
            Wh40kBeginTag(view, new Vector2(labelX, view.Y - dimensions.Y * 0.5f), priority: 4, always: true, key: HashCode.Combine(uid.Id, 991));
            Wh40kTagLine(handle, label, UIScale * 0.85f, color);
        }
    }
}
