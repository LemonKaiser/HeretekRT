using System.Numerics;
using Content.Shared._WH40K.Bosses;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._WH40K.Bosses;

public sealed class Wh40kChaosMarineBarrageOverlay(IEntityManager entities) : Wh40kChaosMarineRocketOverlay(entities, true);
public sealed class Wh40kChaosMarineTargetLockOverlay(IEntityManager entities) : Wh40kChaosMarineRocketOverlay(entities, false);

public abstract class Wh40kChaosMarineRocketOverlay : Overlay
{
    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly Wh40kBossArenaSystem _arena;
    private readonly IPlayerManager _players;
    private readonly Wh40kChaosMarineSystem _visuals;
    private readonly bool _floor;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    protected Wh40kChaosMarineRocketOverlay(IEntityManager entities, bool floor)
    {
        _entities = entities;
        _transform = entities.System<SharedTransformSystem>();
        _arena = entities.System<Wh40kBossArenaSystem>();
        _players = IoCManager.Resolve<IPlayerManager>();
        _visuals = entities.System<Wh40kChaosMarineSystem>();
        _floor = floor;
        ZIndex = (int) (floor ? Content.Shared.DrawDepth.DrawDepth.FloorObjects : Content.Shared.DrawDepth.DrawDepth.Mobs) + 1;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_players.LocalEntity is not { } player) return;
        var h = args.WorldHandle;
        var previous = h.GetTransform();
        var query = _entities.EntityQueryEnumerator<Wh40kChaosMarineComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var state, out var xform))
        {
            if (xform.MapID != args.MapId || !_arena.Contains(uid, player) ||
                state.Anchor is not { } anchor || !_entities.EntityExists(anchor)) continue;
            if (_floor && Wh40kChaosMarineCombat.IsBarrage(state.Attack))
            {
                h.SetTransform(_transform.GetWorldMatrix(anchor));
                var preparation = Math.Max(.01, (state.ExecuteAt - state.StartedAt).TotalSeconds);
                var charge = (float) Math.Clamp((_visuals.AnimationTime(uid) - state.StartedAt).TotalSeconds / preparation, 0, 1);
                h.UseShader(_visuals.RocketMarkShader());
                for (var i = 0; i < state.Points.Length; i++)
                    if (!state.ResolvedRockets.Contains(i)) DrawMark(h, state.Points[i], false, charge);
                h.UseShader(null);
                h.SetTransform(previous);
            }
            else if (!_floor && state.Attack == MarineAttack.S1)
                foreach (var target in state.RocketTargets)
                {
                    if (!_entities.TryGetComponent<TransformComponent>(target, out var tx) || tx.MapID != args.MapId ||
                        !_arena.Contains(uid, target)) continue;
                    var rotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;
                    h.SetTransform(Matrix3x2.CreateRotation((float) -rotation) * Matrix3x2.CreateTranslation(_transform.GetWorldPosition(target)));
                    h.UseShader(_visuals.RocketMarkShader());
                    DrawMark(h, Vector2.Zero, true, Wh40kChaosMarineSystem.AttackCharge(state, _visuals.AnimationTime(uid)));
                    h.UseShader(null);
                    h.SetTransform(previous);
                }
        }
        h.SetTransform(previous);
    }

    private static void DrawMark(DrawingHandleWorld h, Vector2 center, bool diamond, float charge)
    {
        // The diamond is centred on the target's body, not half a tile above it.
        var radius = diamond ? .3f : .5f;
        var data = new Vector2(charge, diamond ? 1 : 0);
        DrawVertexUV2DColor[] vertices =
        [
            new(center + new Vector2(-radius, -radius), new Vector2(0, 0), Color.White) { UV2 = data },
            new(center + new Vector2(radius, -radius), new Vector2(1, 0), Color.White) { UV2 = data },
            new(center + new Vector2(radius, radius), new Vector2(1, 1), Color.White) { UV2 = data },
            new(center + new Vector2(-radius, radius), new Vector2(0, 1), Color.White) { UV2 = data },
        ];
        h.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, Texture.White, vertices);
    }
}
