using System.Numerics;
using Content.Shared._WH40K.FtlEffects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.Timing;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WH40K.FtlEffects;

/// <summary>Installs the hull-fitted FTL/Warp departure overlays.</summary>
public sealed partial class Wh40kFtlDepartureSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IClientGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transforms = default!;

    public override void Initialize()
    {
        base.Initialize();
        var background = new Wh40kFtlMotionBackgroundOverlay(this);
        _overlays.AddOverlay(background);
        _overlays.AddOverlay(new Wh40kFtlMotionOverlay(this, background));
        _overlays.AddOverlay(new Wh40kFtlDepartureOverlay(this));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<Wh40kFtlDepartureOverlay>();
        _overlays.RemoveOverlay<Wh40kFtlMotionOverlay>();
        _overlays.RemoveOverlay<Wh40kFtlMotionBackgroundOverlay>();
        base.Shutdown();
    }

    public TimeSpan Now
    {
        get
        {
            var lead = Math.Max(0L, (long) _timing.CurTick.Value - _timing.LastRealTick.Value);
            return _timing.CurTime - TimeSpan.FromTicks(_timing.TickPeriod.Ticks * lead);
        }
    }

    public float Motion(EntityUid grid, Wh40kFtlDepartureComponent effect)
    {
        if (Aboard(grid, effect))
            return 0f;
        return Wh40kFtlDepartureTiming.Motion(Now, effect.Departure, effect.Entered, effect.Arriving);
    }

    public bool Gone(EntityUid grid, Wh40kFtlDepartureComponent effect)
    {
        return !Aboard(grid, effect) && Wh40kFtlDepartureTiming.Gone(Now, effect.Departure, effect.Entered, effect.Arriving);
    }

    public Matrix3x2 Moved(Wh40kFtlDepartureComponent effect, MapGridComponent grid, TransformComponent xform, Matrix3x2 matrix, float motion)
    {
        if (effect.Lead is { } lead && TryComp(lead, out MapGridComponent? leadGrid) &&
            TryComp(lead, out TransformComponent? leadXform) && leadXform.MapID == xform.MapID)
        {
            return Wh40kFtlConeGeometry.FollowerTransform(leadGrid.LocalAABB, motion, matrix,
                _transforms.GetWorldMatrix(leadXform));
        }

        return Wh40kFtlConeGeometry.MotionTransform(grid.LocalAABB, motion) * matrix;
    }

    public bool HasMotion(MapId map, Box2 view)
    {
        var query = EntityQueryEnumerator<Wh40kFtlDepartureComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var effect, out var grid, out var xform))
        {
            var motion = Motion(uid, effect);
            if (xform.MapID != map || MathF.Abs(motion) < 0.0001f && !Gone(uid, effect))
                continue;
            var matrix = _transforms.GetWorldMatrix(xform);
            var moved = Moved(effect, grid, xform, matrix, motion);
            if (view.Intersects(matrix.TransformBox(grid.LocalAABB.Enlarged(0.5f))) ||
                view.Intersects(moved.TransformBox(grid.LocalAABB.Enlarged(0.5f))))
                return true;
        }

        return false;
    }

    private bool Aboard(EntityUid grid, Wh40kFtlDepartureComponent effect)
    {
        if (_player.LocalEntity is not { } player)
            return false;

        var viewed = TryComp(player, out EyeComponent? eye) && eye.Target is { } target && Exists(target)
            ? target
            : player;

        if (!TryComp(viewed, out TransformComponent? xform) || xform.GridUid is not { } deck)
            return false;
        var lead = effect.Lead ?? grid;
        return deck == grid || deck == lead || TryComp(deck, out Wh40kFtlDepartureComponent? own) && own.Lead == lead;
    }
}
