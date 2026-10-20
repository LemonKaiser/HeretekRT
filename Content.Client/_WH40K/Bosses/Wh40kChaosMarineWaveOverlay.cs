using System.Numerics;
using Content.Client._WH40K.Visuals.WorldEffects;
using Content.Shared._WH40K.Bosses;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Bosses;

public sealed class Wh40kChaosMarineWaveOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Wave = "Wh40kMarineLandingWave";
    private static readonly ProtoId<ShaderPrototype> Transition = "Wh40kMarineTransitionWave";
    private readonly IEntityManager _entities;
    private readonly Wh40kBossArenaSystem _arena;
    private readonly Wh40kChaosMarineSystem _visuals;
    private readonly SharedTransformSystem _transform;
    private readonly IPlayerManager _players;
    private readonly IPrototypeManager _prototypes;
    private readonly Dictionary<EntityUid, ShaderInstance> _shaders = new();
    private readonly List<EntityUid> _removed = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;
    public override bool RequestScreenTexture => true;

    public Wh40kChaosMarineWaveOverlay(IEntityManager entities)
    {
        _entities = entities;
        _arena = entities.System<Wh40kBossArenaSystem>();
        _visuals = entities.System<Wh40kChaosMarineSystem>();
        _transform = entities.System<SharedTransformSystem>();
        _players = IoCManager.Resolve<IPlayerManager>();
        _prototypes = IoCManager.Resolve<IPrototypeManager>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _removed.Clear();
        foreach (var (uid, _) in _shaders)
        {
            if (_entities.HasComponent<Wh40kMarineEffectComponent>(uid)) continue;
            if (_entities.TryGetComponent<Wh40kChaosMarineComponent>(uid, out var state) &&
                Wh40kChaosMarineTransitionVisuals.Elapsed(state, _visuals.AnimationTime(uid)) >= 0) continue;
            _removed.Add(uid);
        }
        foreach (var uid in _removed)
        {
            _shaders[uid].Dispose();
            _shaders.Remove(uid);
        }
        if (args.Viewport.Eye == null || _players.LocalEntity is not { } player) return false;
        var query = _entities.EntityQueryEnumerator<Wh40kMarineEffectComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var effect, out var xform))
        {
            var now = _visuals.AnimationTime(uid);
            if (effect.Kind == MarineEffect.Landing && xform.MapID == args.MapId &&
                now >= effect.StartedAt && now < effect.EndsAt && effect.Boss is { } boss &&
                _arena.Contains(boss, player) &&
                args.WorldAABB.Enlarged(effect.Radius).Contains(_transform.GetWorldPosition(xform))) return true;
        }
        var bosses = _entities.EntityQueryEnumerator<Wh40kChaosMarineComponent, TransformComponent>();
        while (bosses.MoveNext(out var uid, out var state, out var xform))
            if (TryTransition(uid, state, xform, args, player, out _, out _)) return true;
        return false;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || args.Viewport.Eye == null || _players.LocalEntity is not { } player) return;
        var h = args.WorldHandle;
        var previous = h.GetTransform();
        h.SetTransform(Matrix3x2.Identity);
        var query = _entities.EntityQueryEnumerator<Wh40kMarineEffectComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var effect, out var xform))
        {
            if (effect.Kind != MarineEffect.Landing || xform.MapID != args.MapId ||
                effect.Boss is not { } boss || !_arena.Contains(boss, player)) continue;
            var now = _visuals.AnimationTime(uid);
            var progress = (float) ((now - effect.StartedAt).TotalSeconds / Math.Max(.01, (effect.EndsAt - effect.StartedAt).TotalSeconds));
            if (progress is < 0 or >= 1) continue;
            var position = _transform.GetWorldPosition(xform);
            if (!args.WorldAABB.Enlarged(effect.Radius).Contains(position)) continue;
            DrawWave(args, uid, Wave, position, effect.Radius, "Progress", progress, 1);
        }
        var bosses = _entities.EntityQueryEnumerator<Wh40kChaosMarineComponent, TransformComponent>();
        while (bosses.MoveNext(out var uid, out var state, out var xform))
            if (TryTransition(uid, state, xform, args, player, out var position, out var elapsed))
                DrawWave(args, uid, Transition, position, Wh40kChaosMarineTransitionVisuals.Radius,
                    "PhaseTime", elapsed, _entities.GetComponent<SpriteComponent>(uid).Color.A);
        h.UseShader(null);
        h.SetTransform(previous);
    }

    private bool TryTransition(EntityUid uid, Wh40kChaosMarineComponent state, TransformComponent xform,
        in OverlayDrawArgs args, EntityUid player, out Vector2 position, out float elapsed)
    {
        position = default;
        elapsed = Wh40kChaosMarineTransitionVisuals.Elapsed(state, _visuals.AnimationTime(uid));
        if (elapsed < 0 || xform.MapID != args.MapId || state.Anchor is not { } anchor || !_entities.EntityExists(anchor) ||
            !_arena.Contains(uid, player) || !_entities.TryGetComponent<SpriteComponent>(uid, out var sprite) ||
            !_visuals.CanDrawEffects(uid, sprite, xform) || sprite[0] is not SpriteComponent.Layer layer ||
            args.Viewport.Eye is not { } eye) return false;
        var rotation = _transform.GetWorldRotation(xform);
        var facing = Wh40kChaosMarineJumpVisuals.Facing(sprite, rotation, eye.Rotation);
        var matrix = SpriteLayerWorldTransform.Get(sprite, layer, facing,
            _transform.GetWorldPosition(xform), rotation, rotation + eye.Rotation, eye.Rotation);
        position = Vector2.Transform(Vector2.Zero, matrix);
        return args.WorldAABB.Enlarged(Wh40kChaosMarineTransitionVisuals.Radius).Contains(position);
    }

    private void DrawWave(in OverlayDrawArgs args, EntityUid uid, ProtoId<ShaderPrototype> prototype,
        Vector2 position, float radius, string timeParameter, float time, float alpha)
    {
        if (!_shaders.TryGetValue(uid, out var shader))
            _shaders[uid] = shader = _prototypes.Index(prototype).InstanceUnique();
        var screen = args.Viewport.WorldToLocal(position);
        screen.Y = args.Viewport.Size.Y - screen.Y;
        shader.SetParameter("SCREEN_TEXTURE", ScreenTexture!);
        shader.SetParameter("CenterPixels", screen);
        shader.SetParameter(timeParameter, time);
        if (prototype == Transition && _entities.TryGetComponent<Wh40kChaosMarineComponent>(uid, out var state) &&
            state.Anchor is { } anchor)
        {
            var origin = _arena.World(anchor, state.Origin);
            var center = args.Viewport.WorldToLocal(origin);
            var radiusPixels = Vector2.Distance(center, args.Viewport.WorldToLocal(origin + Vector2.UnitX)) *
                Wh40kChaosMarineCombat.TransitionBurstRadius;
            center.Y = args.Viewport.Size.Y - center.Y;
            shader.SetParameter("BurstCenterPixels", center);
            shader.SetParameter("BurstRadiusPixels", radiusPixels);
            shader.SetParameter("BurstProgress", Wh40kChaosMarineCombat.TransitionBurstProgress(time));
            shader.SetParameter("BurstAge", time - Wh40kChaosMarineCombat.TransitionBurstAt);
            shader.SetParameter("GatherScale", radius / 6);
        }
        args.WorldHandle.UseShader(shader);
        args.WorldHandle.DrawRect(Box2.FromDimensions(position - new Vector2(radius), new Vector2(radius * 2)),
            Color.White.WithAlpha(alpha));
    }

    protected override void DisposeBehavior()
    {
        foreach (var shader in _shaders.Values) shader.Dispose();
        _shaders.Clear();
        base.DisposeBehavior();
    }
}
