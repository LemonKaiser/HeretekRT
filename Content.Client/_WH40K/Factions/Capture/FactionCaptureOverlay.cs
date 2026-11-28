using System.Numerics;
using Content.Shared._WH40K.Factions.Capture;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Factions.Capture;

public sealed partial class FactionCaptureOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize() => _overlays.AddOverlay(new FactionCaptureOverlay(EntityManager, _players, _timing));
    public override void Shutdown() => _overlays.RemoveOverlay<FactionCaptureOverlay>();

    public override void FrameUpdate(float frameTime)
    {
        var blend = 1f - MathF.Exp(-10f * frameTime);
        var points = EntityQueryEnumerator<FactionCapturePointComponent>();
        while (points.MoveNext(out _, out var point))
        {
            var remaining = point.CaptureProgress - point.DisplayedProgress;
            if (point.Status == FactionCaptureStatus.Capturing && remaining > 0f)
                point.DisplayedProgress += remaining * blend;
            else
                point.DisplayedProgress = point.CaptureProgress;
        }
    }
}

internal sealed class FactionCaptureOverlay(IEntityManager entities, IPlayerManager players, IGameTiming timing) : Overlay
{
    private const int RingSegments = 128;
    private readonly SharedTransformSystem _transform = entities.System<SharedTransformSystem>();
    private readonly Vector2[] _arcVertices = new Vector2[(RingSegments + 1) * 2];
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var time = (float) timing.RealTime.TotalSeconds;
        var pulse = (MathF.Sin(time * 2f) + 1f) * 0.5f;
        var player = entities.TryGetComponent(players.LocalEntity, out TransformComponent? local) && local.MapID == args.MapId
            ? local
            : null;
        var playerPosition = player == null ? default : _transform.GetWorldPosition(player);

        var query = entities.EntityQueryEnumerator<FactionCapturePointComponent, TransformComponent>();
        while (query.MoveNext(out _, out var point, out var transform))
        {
            if (!point.Active || transform.MapID != args.MapId)
                continue;

            var center = _transform.GetWorldPosition(transform);
            if (!args.WorldAABB.Enlarged(point.Radius).Contains(center))
                continue;

            var opacity = 1f;
            if (player != null && player.GridUid == transform.GridUid)
            {
                var depth = (point.Radius - Vector2.Distance(playerPosition, center)) / 0.5f;
                opacity -= 0.32f * Math.Clamp(depth, 0f, 1f);
            }

            var progress = Math.Clamp(point.DisplayedProgress, 0f, 1f);
            var capturing = point.Status == FactionCaptureStatus.Capturing;
            var color = point.BoundaryColor;
            var trackColor = capturing ? FactionCapturePointComponent.NeutralColor : color;
            var fillColor = Color.InterpolateBetween(trackColor, color, progress * 0.35f);
            handle.DrawCircle(center, point.Radius, fillColor.WithAlpha(0.05f * opacity * opacity));
            handle.DrawCircle(center, point.Radius, trackColor.WithAlpha(0.78f * opacity), filled: false);
            handle.DrawCircle(center, point.Radius - 0.24f, trackColor.WithAlpha(0.34f * opacity), filled: false);
            for (var i = 0; i < 8; i++)
            {
                var angle = i * MathF.Tau / 8;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                handle.DrawLine(center + direction * (point.Radius - 0.4f), center + direction * point.Radius,
                    trackColor.WithAlpha(0.86f * opacity));
            }

            var accent = Color.InterpolateBetween(color, Color.White, 0.18f);
            if (progress > 0f)
                DrawProgress(handle, center, point.Radius, progress, accent.WithAlpha(0.96f * opacity));

            if (capturing)
            {
                var tip = center + ArcDirection(progress) * (point.Radius - 0.08f);
                handle.DrawCircle(tip, 0.15f + 0.05f * pulse, accent.WithAlpha(0.35f * opacity));
                handle.DrawCircle(tip, 0.075f, accent.WithAlpha(0.96f * opacity));
            }

            var beacon = center + new Vector2(0.30f, 1.45f);
            handle.DrawCircle(beacon, 0.17f + pulse * 0.12f, color.WithAlpha(0.28f * opacity));
            handle.DrawCircle(beacon, 0.08f, color.WithAlpha((0.78f + pulse * 0.22f) * opacity));
        }
    }

    private void DrawProgress(DrawingHandleWorld handle, Vector2 center, float radius, float progress, Color color)
    {
        var segments = (int) MathF.Ceiling(progress * RingSegments);
        for (var i = 0; i <= segments; i++)
        {
            var direction = ArcDirection(progress * i / segments);
            _arcVertices[2 * i] = center + direction * radius;
            _arcVertices[2 * i + 1] = center + direction * (radius - 0.16f);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _arcVertices.AsSpan(0, (segments + 1) * 2), color);
    }

    private static Vector2 ArcDirection(float progress)
    {
        var (sin, cos) = MathF.SinCos(MathF.Tau * progress);
        return new Vector2(sin, cos);
    }
}
