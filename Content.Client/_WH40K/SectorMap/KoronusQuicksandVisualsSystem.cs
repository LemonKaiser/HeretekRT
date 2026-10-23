using System.Numerics;
using Content.Client._WH40K.Visuals.WorldEffects;
using Content.Client.Interactable.Components;
using Content.Shared._WH40K.SectorMap;
using Content.Shared._WH40K.SectorMap.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.SectorMap;

/// <summary>Moves the composed sprite below a fixed sand surface, including clothing and held items.</summary>
public sealed partial class KoronusQuicksandVisualsSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "KoronusQuicksandSinking";

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private RsiVisualGeometryCache _geometry = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KoronusQuicksandSinkingComponent, ComponentRemove>(OnStopSinking);
        SubscribeLocalEvent<KoronusQuicksandVisualsComponent, ComponentShutdown>(OnStopVisuals);
        SubscribeLocalEvent<KoronusQuicksandVisualsComponent, BeforePostShaderRenderEvent>(OnRender);
    }

    public override void FrameUpdate(float frameTime)
    {
        // GetPauseTime also handles a paused entity first entering an observer's PVS.
        var query = AllEntityQuery<KoronusQuicksandSinkingComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var sinking, out var sprite))
            UpdateVisuals(uid, sinking, sprite);
    }

    private void UpdateVisuals(EntityUid uid, KoronusQuicksandSinkingComponent sinking, SpriteComponent sprite)
    {
        if (sinking.SpeedModifier == 1f)
        {
            RemComp<KoronusQuicksandVisualsComponent>(uid);
            return;
        }

        if (!TryComp<KoronusQuicksandVisualsComponent>(uid, out var visuals))
        {
            visuals = AddComp<KoronusQuicksandVisualsComponent>(uid);
            visuals.Shader = _prototypes.Index(ShaderId).InstanceUnique();
        }

        var now = _timing.CurTime - _metadata.GetPauseTime(uid);
        var progress = (float) Math.Clamp((now - sinking.EnteredAt) / KoronusQuicksandSystem.SinkDuration, 0, 1);
        visuals.Progress = progress * progress * (3f - 2f * progress);

        // Hover outlines must not expose the submerged sprite or prevent the mask from starting.
        if (sprite.PostShader != null && sprite.PostShader != visuals.Shader &&
            TryComp<InteractionOutlineComponent>(uid, out var outline))
            outline.OnMouseLeave(uid);

        // The engine has one post-shader slot. Never reveal an invisible character or replace another effect.
        if (sprite.PostShader != null)
            return;

        if (!visuals.ShaderApplied)
        {
            visuals.OriginalShaderEvent = sprite.RaiseShaderEvent;
            visuals.OriginalScreenTexture = sprite.GetScreenTexture;
            visuals.ShaderApplied = true;
        }
        sprite.PostShader = visuals.Shader;
        sprite.RaiseShaderEvent = true;
        sprite.GetScreenTexture = false;
    }

    private void OnRender(Entity<KoronusQuicksandVisualsComponent> ent, ref BeforePostShaderRenderEvent args)
    {
        if (args.Sprite.PostShader != ent.Comp.Shader || args.Viewport.Eye is not { } eye)
            return;

        var (position, rotation) = _transform.GetWorldPositionRotation(ent);
        var projected = new ContactShadowProjectedBounds();
        var screenAngle = (rotation + eye.Rotation).Reduced().FlipPositive();
        var worldToScreen = args.Viewport.GetWorldToLocalMatrix();
        foreach (var candidate in args.Sprite.AllLayers)
        {
            if (candidate is not SpriteComponent.Layer layer || !layer.Visible || layer.Blank ||
                layer.Color.A <= 0.01f || layer.CopyToShaderParameters != null || layer.ActualState is not { } state)
                continue;

            var direction = SpriteComponent.Layer.GetDirection(state.RsiDirections, screenAngle);
            var textureDirection = args.Sprite.EnableDirectionOverride
                ? args.Sprite.DirectionOverride.Convert(state.RsiDirections) : direction;
            var opaque = _geometry.GetOpaqueGeometry(state, textureDirection.OffsetRsiDir(layer.DirOffset), layer.AnimationFrame);
            if (!opaque.Visible)
                continue;

            var matrix = SpriteLayerWorldTransform.Get(args.Sprite, layer, direction, position, rotation, screenAngle, eye.Rotation);
            projected.Include(opaque.Bounds, matrix * worldToScreen, Vector2.UnitX, Vector2.UnitY);
        }

        var top = projected.Valid ? projected.MinY : float.PositiveInfinity;
        var bottom = projected.Valid ? projected.MaxY : float.NegativeInfinity;
        if (!projected.Valid)
        {
            var bounds = _sprites.CalculateBounds((ent.Owner, args.Sprite), position, rotation, eye.Rotation);
            foreach (var corner in new[] { bounds.BottomLeft, bounds.BottomRight, bounds.TopLeft, bounds.TopRight })
            {
                var y = args.Viewport.WorldToLocal(corner).Y;
                top = MathF.Min(top, y);
                bottom = MathF.Max(bottom, y);
            }
        }

        // Parameters use the active viewport, so zoom, rotated cameras and differently sized mobs agree.
        ent.Comp.Shader.SetParameter("SinkPixels", (bottom - top + 1f) * ent.Comp.Progress);
        ent.Comp.Shader.SetParameter("SurfaceY", args.Viewport.Size.Y - bottom);
    }

    private void OnStopSinking(Entity<KoronusQuicksandSinkingComponent> ent, ref ComponentRemove args)
    {
        RemComp<KoronusQuicksandVisualsComponent>(ent);
    }

    private void OnStopVisuals(Entity<KoronusQuicksandVisualsComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite) && sprite.PostShader == ent.Comp.Shader)
        {
            sprite.PostShader = null;
            sprite.RaiseShaderEvent = ent.Comp.OriginalShaderEvent;
            sprite.GetScreenTexture = ent.Comp.OriginalScreenTexture;
        }
        ent.Comp.Shader?.Dispose();
    }
}
