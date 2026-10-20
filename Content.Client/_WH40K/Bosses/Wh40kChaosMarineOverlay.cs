using System.Numerics;
using Content.Shared._WH40K.Bosses;
using Content.Client._WH40K.Visuals.WorldEffects;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._WH40K.Bosses;

public abstract class Wh40kChaosMarineOverlay : Overlay
{
    private readonly IEntityManager _entities;
    private readonly Wh40kBossArenaSystem _arena;
    private readonly Wh40kChaosMarineSystem _visuals;
    private readonly SharedTransformSystem _transform;
    private readonly IPlayerManager _players;
    private readonly bool _above;
    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public Wh40kChaosMarineOverlay(IEntityManager entities, bool above)
    {
        _entities = entities;
        _arena = entities.System<Wh40kBossArenaSystem>();
        _visuals = entities.System<Wh40kChaosMarineSystem>();
        _transform = entities.System<SharedTransformSystem>();
        _players = IoCManager.Resolve<IPlayerManager>();
        _above = above;
        ZIndex = (int) Content.Shared.DrawDepth.DrawDepth.Mobs + (above ? 1 : -1);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_players.LocalEntity is not { } player) return;
        var handle = args.WorldHandle;
        var query = _entities.EntityQueryEnumerator<Wh40kChaosMarineComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var state, out var xform))
        {
            if (xform.MapID != args.MapId || state.Anchor is not { } anchor || !_entities.EntityExists(anchor) ||
                !_arena.Contains(uid, player)) continue;
            if (!_entities.TryGetComponent<SpriteComponent>(uid, out var sprite) ||
                !_visuals.CanDrawEffects(uid, sprite, xform)) continue;
            DrawBoss(handle, uid, state, args);
        }
        if (_above) return;
        var effects = _entities.EntityQueryEnumerator<Wh40kMarineEffectComponent, TransformComponent>();
        while (effects.MoveNext(out var uid, out var effect, out var xform))
        {
            if (xform.MapID != args.MapId || effect.Boss is not { } boss || !_arena.Contains(boss, player)) continue;
            var previous = handle.GetTransform();
            if (effect.Kind == MarineEffect.BarrageImpact)
            {
                // The one-tile impact uses the same grid axes and centre as its floor marker.
                handle.SetTransform(_transform.GetWorldMatrix(xform.ParentUid));
                DrawEffect(handle, effect, xform.LocalPosition, _visuals.AnimationTime(uid));
            }
            else DrawEffect(handle, effect, _transform.GetWorldPosition(xform), _visuals.AnimationTime(uid));
            handle.SetTransform(previous);
        }
    }

    private void DrawBoss(DrawingHandleWorld h, EntityUid uid, Wh40kChaosMarineComponent state, in OverlayDrawArgs args)
    {
        var now = _visuals.AnimationTime(uid);
        var body = _transform.GetWorldPosition(uid);
        var exhaust = Wh40kChaosMarineJumpVisuals.Exhaust(state, now);
        if (exhaust > .001f) DrawExhaust(h, uid, now, exhaust, args);
        if (!_above)
        {
            if (state.Attack == MarineAttack.R2 && state.Anchor is { } anchor)
            {
                var origin = _arena.World(anchor, state.Origin);
                for (var i = state.Shot; i < state.Points.Length; i++)
                {
                    var delta = _arena.World(anchor, state.Points[i]) - origin;
                    var end = origin + delta * .5f;
                    Line(h, origin, end, new Color(1f, .04f, .08f, i == state.Shot ? .65f : .3f), .025f);
                }
            }
            if (now < state.RecoverUntil)
            {
                for (var i = 0; i < 2; i++)
                {
                    var p = ((float) now.TotalSeconds * .8f + i * .5f) % 1;
                    DrawBurst(h, body + new Vector2((i - .5f) * .35f, .65f + p * .35f), .2f, p,
                        new Color(.5f, .45f, .4f, .25f));
                }
            }
        }
        else if (Wh40kChaosMarineCombat.IsRocket(state.Attack))
        {
            var charge = Wh40kChaosMarineSystem.AttackCharge(state, now);
            for (var i = -1; i <= 1; i += 2)
                DrawBurst(h, body + new Vector2(i * .4f, .8f), .2f, .35f,
                    new Color(1f, .25f, .05f, charge * .55f));
        }
        if (state.Mace != null)
            DrawWeapon(h, uid, state, now);
    }

    private void DrawExhaust(DrawingHandleWorld h, EntityUid uid, TimeSpan now, float strength, in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not { } eye ||
            !_entities.TryGetComponent<SpriteComponent>(uid, out var sprite) ||
            sprite[0] is not SpriteComponent.Layer layer) return;
        var rotation = _transform.GetWorldRotation(uid);
        var facing = Wh40kChaosMarineJumpVisuals.Facing(sprite, rotation, eye.Rotation);
        var matrix = SpriteLayerWorldTransform.Get(sprite, layer, facing,
            _transform.GetWorldPosition(uid), rotation, rotation + eye.Rotation, eye.Rotation);
        var scale = new Vector2(new Vector2(matrix.M11, matrix.M12).Length(),
            new Vector2(matrix.M21, matrix.M22).Length());
        var previous = h.GetTransform();
        for (var side = 0; side < 2; side++)
        {
            var mount = Wh40kChaosMarineJumpVisuals.Mount(facing, side);
            if (mount.AboveBody != _above) continue;
            var position = Vector2.Transform(mount.Position, matrix);
            h.SetTransform(Matrix3x2.CreateScale(scale * mount.Scale) *
                Matrix3x2.CreateRotation((float) -eye.Rotation) * Matrix3x2.CreateTranslation(position));
            h.UseShader(_visuals.ExhaustShader(uid, side, strength, now));
            h.DrawRect(new Box2(-.4f, -1.25f, .4f, .15f), Color.White.WithAlpha(sprite.Color.A));
        }
        h.UseShader(null);
        h.SetTransform(previous);
    }

    private void DrawBurst(DrawingHandleWorld h, Vector2 position, float radius, float progress, Color color, bool barrage = false)
    {
        h.UseShader(_visuals.ImpactShader());
        // Progress belongs to each quad, so deferred draws cannot overwrite another effect's age.
        var age = new Vector2(progress, barrage ? 1 : 0);
        DrawVertexUV2DColor[] vertices =
        [
            new(position + new Vector2(-radius, -radius), new Vector2(0, 0), color) { UV2 = age },
            new(position + new Vector2(radius, -radius), new Vector2(1, 0), color) { UV2 = age },
            new(position + new Vector2(radius, radius), new Vector2(1, 1), color) { UV2 = age },
            new(position + new Vector2(-radius, radius), new Vector2(0, 1), color) { UV2 = age },
        ];
        h.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, Texture.White, vertices);
        h.UseShader(null);
    }

    private void DrawWeapon(DrawingHandleWorld h, EntityUid uid, Wh40kChaosMarineComponent state, TimeSpan now)
    {
        if (state.Mace is not { } head || !_entities.EntityExists(head)) return;
        var hand = state.MaceGrip is { } grip && _entities.EntityExists(grip) ? _transform.GetWorldPosition(grip) :
            _transform.GetWorldPosition(uid) + Wh40kChaosMarineWeaponAnimation.Grip(_transform.GetWorldRotation(uid).ToWorldVec());
        var from = hand;
        var previous = h.GetTransform();
        var chain = _visuals.ChainLinkTexture();
        h.UseShader(_visuals.WeaponShader(uid, state, now));
        // The ten control points are actual replicated bodies joined by native distance joints.
        // Small alternating links follow their bends, rather than a hand-to-head straight line.
        for (var part = 0; part <= state.MaceLinks.Count; part++)
        {
            var entity = part < state.MaceLinks.Count ? state.MaceLinks[part] : head;
            if (!_entities.EntityExists(entity)) break;
            var end = _transform.GetWorldPosition(entity);
            if (entity == head)
                end += _transform.GetWorldRotation(head).RotateVec(new Vector2(0, -.2f));
            var count = Math.Clamp((int) MathF.Ceiling(Vector2.Distance(from, end) / .12f), 1, 12);
            for (var link = 0; link < count; link++)
            {
                var start = Vector2.Lerp(from, end, link / (float) count);
                var to = Vector2.Lerp(from, end, (link + 1f) / count);
                var pos = (start + to) * .5f;
                if ((pos.Y <= hand.Y) != _above) continue;
                var delta = to - start;
                var angle = MathF.Atan2(delta.Y, delta.X) - MathF.PI / 2;
                var width = (part + link) % 2 == 0 ? .035f : .023f;
                h.SetTransform(Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateTranslation(pos));
                h.DrawTextureRect(chain, new Box2(-width, -delta.Length() * .58f, width, delta.Length() * .58f));
            }
            from = end;
        }
        h.SetTransform(previous);
        h.UseShader(null);
        // The head is a normal rotating Sprite attached to its physical entity, drawn by SpriteSystem.
    }

    private void DrawEffect(DrawingHandleWorld h, Wh40kMarineEffectComponent effect, Vector2 position, TimeSpan now)
    {
        if (effect.Kind is MarineEffect.Landing or MarineEffect.Summon) return;
        var duration = MathF.Max(.01f, (float) (effect.EndsAt - effect.StartedAt).TotalSeconds);
        var p = Math.Clamp((float) (now - effect.StartedAt).TotalSeconds / duration, 0, 1);
        var color = new Color(1f, .22f + .3f * (1 - p), .06f, 1 - p);
        if (effect.Kind == MarineEffect.Seal)
        {
            Rune(h, position, effect.Radius, new Color(.95f, .05f, .08f, .5f + .2f * MathF.Sin((float) now.TotalSeconds * 8)), 0);
            Arc(h, position, effect.Radius * 1.05f, 0, p * MathF.Tau, color, .04f);
            return;
        }
        DrawBurst(h, position, MathF.Max(.25f, effect.Radius), p, color, effect.Kind == MarineEffect.BarrageImpact);
    }

    private static Vector2 Unit(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));

    internal static void Line(DrawingHandleWorld h, Vector2 from, Vector2 to, Color color, float width)
    {
        var delta = to - from;
        if (delta.LengthSquared() < .00001f) return;
        var normal = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * width * .5f;
        Vector2[] points = [from + normal, to + normal, to - normal, from - normal];
        h.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, points, color);
    }

    private static void Arc(DrawingHandleWorld h, Vector2 center, float radius, float start, float end, Color color, float width)
    {
        var count = Math.Max(1, (int) (MathF.Abs(end - start) * 12));
        for (var i = 0; i < count; i++)
            Line(h, center + Unit(start + (end - start) * i / count) * radius,
                center + Unit(start + (end - start) * (i + 1) / count) * radius, color, width);
    }

    private static void Rune(DrawingHandleWorld h, Vector2 center, float radius, Color color, int count)
    {
        h.DrawCircle(center, radius, color, false);
        for (var i = 0; i < 6; i++)
        {
            var a = i * MathF.Tau / 6;
            Line(h, center + Unit(a) * radius, center + Unit(a + MathF.PI / 2) * radius * .7f, color.WithAlpha(color.A * .6f), .025f);
        }
        for (var i = 0; i <= count; i++)
            h.DrawCircle(center + new Vector2((i - count / 2f) * .2f, 0), .055f, color);
    }
}
