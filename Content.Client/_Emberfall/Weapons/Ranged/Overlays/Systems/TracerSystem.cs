using System.Numerics;
using Content.Client._Emberfall.Weapons.Ranged.Overlays;
using Content.Client._WH40K.Visuals.WorldEffects;
using Content.Shared._Emberfall.Weapons.Ranged;
using Content.Shared._WH40K.Weapons;
using Content.Shared.Projectiles;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._Emberfall.Weapons.Ranged.Systems;

public sealed partial class TracerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    private TracerOverlay _tracerOverlay = default!;
    private WH40KPelletTracerRenderer _pellets = default!;

    public override void Initialize()
    {
        base.Initialize();
        _pellets = new WH40KPelletTracerRenderer(EntityManager, _transform);
        _tracerOverlay = new TracerOverlay(this);
        _overlay.AddOverlay(_tracerOverlay);

        SubscribeLocalEvent<TracerComponent, ComponentStartup>(OnTracerStart);
    }

    public override void Shutdown()
    {
        _overlay.RemoveOverlay(_tracerOverlay);
        base.Shutdown();
    }

    private void OnTracerStart(Entity<TracerComponent> ent, ref ComponentStartup args)
    {
        var xform = Transform(ent);
        var pos = _transform.GetWorldPosition(xform);

        ent.Comp.Data = new TracerData(
            new List<Vector2> { pos },
            _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.Lifetime)
        );
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<TracerComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var tracer, out var xform))
        {
            if (TryComp<ProjectileComponent>(uid, out var projectile) && projectile.PelletVisual != WH40KPelletVisual.Default)
                continue;

            if (curTime > tracer.Data.EndTime)
            {
                RemCompDeferred<TracerComponent>(uid);
                continue;
            }

            var currentPos = _transform.GetWorldPosition(xform);
            tracer.Data.PositionHistory.Add(currentPos);

            while (tracer.Data.PositionHistory.Count > 2 &&
                   GetTrailLength(tracer.Data.PositionHistory) > tracer.Length)
            {
                tracer.Data.PositionHistory.RemoveAt(0);
            }
        }
    }

    private static float GetTrailLength(List<Vector2> positions)
    {
        var length = 0f;
        for (var i = 1; i < positions.Count; i++)
        {
            length += Vector2.Distance(positions[i - 1], positions[i]);
        }
        return length;
    }

    public void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var query = EntityQueryEnumerator<TracerComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var tracer, out var xform))
        {
            if (xform.MapID != args.MapId ||
                TryComp<ProjectileComponent>(uid, out var projectile) && projectile.PelletVisual != WH40KPelletVisual.Default)
                continue;

            var positions = tracer.Data.PositionHistory;

            if (positions.Count < 2)
                continue;

            handle.SetTransform(Matrix3x2.Identity);

            for (var i = 1; i < positions.Count; i++)
            {
                handle.DrawLine(positions[i - 1], positions[i], tracer.Color);
            }
        }

        _pellets.Draw(in args);
    }
}
