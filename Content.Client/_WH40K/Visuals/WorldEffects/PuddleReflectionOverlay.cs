using System.Numerics;
using Content.Shared._WH40K.Invisibility;
using Content.Shared.Buckle.Components;
using Content.Shared.Fluids.Components;
using Content.Shared.Humanoid;
using Content.Shared.Standing;
using Content.Shared.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WH40K.Visuals.WorldEffects;

public sealed partial class PuddleReflectionOverlay : Overlay
{
    private const int MaxReflections = 48;
    private const int StencilBit = 0x80;
    private const float FallbackFootHeight = -0.42f;
    private const float ReflectionScale = 0.91f;
    private static readonly ProtoId<ShaderPrototype> StencilMask = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilClear = "StencilClear";
    private static readonly ProtoId<ShaderPrototype> WaterReflection = "WH40KPuddleReflection";

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private RsiVisualGeometryCache _geometry = default!;

    private readonly EntityLookupSystem _lookup;
    private readonly SharedTransformSystem _transform;
    private readonly OccluderSystem _occluders;
    private readonly SpriteSystem _sprites;
    private readonly ShaderInstance _mask;
    private readonly ShaderInstance _clearMask;
    private readonly ShaderPrototype _reflectionShader;
    private readonly List<ShaderInstance> _drawShaders = new();
    private readonly HashSet<Entity<HumanoidAppearanceComponent>> _humanoids = new();
    private readonly HashSet<Entity<PuddleComponent>> _puddles = new();
    private readonly List<PuddleMask> _masks = new();
    private readonly List<SpriteComponent.Layer> _layers = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public PuddleReflectionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _lookup = _entities.System<EntityLookupSystem>();
        _transform = _entities.System<SharedTransformSystem>();
        _occluders = _entities.System<OccluderSystem>();
        _sprites = _entities.System<SpriteSystem>();

        var mask = _prototypes.Index(StencilMask).Instance();
        _mask = mask.Duplicate();
        _mask.Stencil = mask.Stencil with { Ref = StencilBit, WriteMask = StencilBit };

        var clear = _prototypes.Index(StencilClear).Instance();
        _clearMask = clear.Duplicate();
        _clearMask.Stencil = clear.Stencil with { WriteMask = StencilBit };

        _reflectionShader = _prototypes.Index(WaterReflection);

        ZIndex = (int) DrawDepth.Puddles + 2;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace || args.Viewport.Eye is not { } eye)
            return;

        _humanoids.Clear();
        _lookup.GetEntitiesIntersecting(args.MapId, args.WorldAABB.Enlarged(1f), _humanoids,
            LookupFlags.Uncontained | LookupFlags.Approximate);

        var drawCount = 0;
        var eyeRotation = eye.Rotation;
        var toView = Matrix3x2.CreateRotation((float) eyeRotation.Theta);
        var toWorld = Matrix3x2.CreateRotation((float) -eyeRotation.Theta);
        var screenRight = Vector2.TransformNormal(Vector2.UnitX, toWorld);
        var screenUp = Vector2.TransformNormal(Vector2.UnitY, toWorld);
        var worldToViewport = args.Viewport.GetWorldToLocalMatrix();
        var pixelsPerTexel = -Vector2.TransformNormal(screenUp, worldToViewport).Y / EyeManager.PixelsPerMeter;
        foreach (var (uid, _) in _humanoids)
        {
            if (!_entities.TryGetComponent(uid, out SpriteComponent? sprite) ||
                !sprite.Visible || sprite.ContainerOccluded || sprite.Color.A <= 0.01f ||
                !_entities.TryGetComponent(uid, out TransformComponent? xform) ||
                xform.GridUid is not { } grid || xform.ParentUid != grid ||
                _entities.GetComponent<MetaDataComponent>(uid).VisibilityMask != 1 ||
                _entities.HasComponent<WH40KPsykerInvisibilityComponent>(uid) ||
                _entities.TryGetComponent(uid, out StealthComponent? stealth) && stealth.Enabled ||
                _entities.TryGetComponent(uid, out BuckleComponent? buckle) && buckle.Buckled)
                continue;

            var (position, rotation) = _transform.GetWorldPositionRotation(xform);
            var screenAngle = (rotation + eyeRotation).Reduced().FlipPositive();
            var lying = _entities.TryGetComponent(uid, out StandingStateComponent? standing) &&
                        (standing.CurrentState == StandingState.Lying ||
                         standing.CurrentState == StandingState.GettingUp && Math.Abs(Math.Sin(sprite.Rotation.Theta)) > 0.5);
            var contact = lying
                ? GetLyingBounds((uid, sprite), position, rotation, screenAngle, eyeRotation, screenRight, screenUp)
                : SpriteLayerWorldTransform.GetFootBounds(_sprites, _geometry, (uid, sprite), position, rotation,
                    screenAngle, eyeRotation, screenRight, screenUp);
            var contactHeight = contact.Valid
                ? contact.MinY - Vector2.Dot(position, screenUp)
                : lying ? -0.16f : FallbackFootHeight;
            var pivot = position + screenUp * contactHeight;
            var mirror = new Matrix3x2(1f, 0f, 0f, lying ? -1f : -ReflectionScale, 0f, 0f);
            var reflection = Matrix3x2.CreateTranslation(-pivot) * toView * mirror * toWorld *
                             Matrix3x2.CreateTranslation(pivot);
            var bounds = reflection.TransformBox(_sprites.CalculateBounds((uid, sprite), position, rotation, eyeRotation))
                .Enlarged(1f / EyeManager.PixelsPerMeter);
            if (!bounds.Intersects(args.WorldAABB) || !FindPuddleMasks(grid, bounds, eyeRotation, out var maskBounds))
                continue;

            if (eye.DrawFov && !_occluders.InRangeUnoccluded(eye.Position, new MapCoordinates(position, args.MapId),
                    range: 0f, ignoreTouching: false))
                continue;

            var handle = args.WorldHandle;
            // Other rendering passes can leave stencil bits set. Own one bit only for this reflection.
            handle.SetTransform(Matrix3x2.Identity);
            handle.UseShader(_clearMask);
            handle.DrawRect(maskBounds, Color.White);

            handle.UseShader(_mask);
            foreach (var mask in _masks)
            {
                handle.SetTransform(mask.Transform);
                handle.DrawTextureRect(mask.Texture, mask.Bounds);
            }

            var shader = GetDrawShader(drawCount);
            var footPixel = args.Viewport.Size.Y - Vector2.Transform(pivot, worldToViewport).Y;
            shader.SetParameter("ReflectionGeometry", new Vector2(footPixel, pixelsPerTexel));
            handle.UseShader(shader);

            _layers.Clear();
            foreach (var entry in sprite.AllLayers)
            {
                if (entry is SpriteComponent.Layer { Visible: true, Blank: false, CopyToShaderParameters: null } layer)
                    _layers.Add(layer);
            }

            // The first opaque texel claims the stencil pixel, so clothing and skin do not blend through one another.
            for (var i = _layers.Count - 1; i >= 0; i--)
            {
                var layer = _layers[i];
                if (layer.Color.A <= 0.01f || GetLayerTexture(sprite, layer, screenAngle, out var direction) is not { } texture)
                    continue;

                var transform = SpriteLayerWorldTransform.Get(sprite, layer, direction, position, rotation, screenAngle, eyeRotation);
                handle.SetTransform(transform * reflection);
                handle.DrawTextureRect(texture, GetTextureBounds(texture), sprite.Color * layer.Color);
            }

            handle.SetTransform(Matrix3x2.Identity);
            handle.UseShader(_clearMask);
            handle.DrawRect(maskBounds, Color.White);
            if (++drawCount == MaxReflections)
                break;
        }

        args.WorldHandle.UseShader(null);
    }

    private ContactShadowProjectedBounds GetLyingBounds(Entity<SpriteComponent> entity, Vector2 position,
        Angle rotation, Angle screenAngle, Angle eyeRotation, Vector2 screenRight, Vector2 screenUp)
    {
        var bounds = new ContactShadowProjectedBounds();
        if (_sprites.TryGetLayer(entity.AsNullable(), HumanoidVisualLayers.Chest, out var chest, logMissing: false))
            SpriteLayerWorldTransform.IncludeOpaqueLayer(_geometry, entity.Comp, chest, position, rotation, screenAngle,
                eyeRotation, screenRight, screenUp, ref bounds, allowHidden: true);
        if (_sprites.TryGetLayer(entity.AsNullable(), HumanoidVisualLayers.Head, out var head, logMissing: false))
            SpriteLayerWorldTransform.IncludeOpaqueLayer(_geometry, entity.Comp, head, position, rotation, screenAngle,
                eyeRotation, screenRight, screenUp, ref bounds, allowHidden: true);
        return bounds;
    }

    private ShaderInstance GetDrawShader(int index)
    {
        // Queued draws read shader parameters when they flush. Each visible reflection needs its own instance.
        if (index == _drawShaders.Count)
        {
            var shader = _reflectionShader.InstanceUnique();
            shader.Stencil = new StencilParameters
            {
                Enabled = true,
                Ref = StencilBit,
                ReadMask = StencilBit,
                WriteMask = StencilBit,
                Func = StencilFunc.Equal,
                Op = StencilOp.Zero,
            };
            _drawShaders.Add(shader);
        }

        return _drawShaders[index];
    }

    private bool FindPuddleMasks(EntityUid grid, Box2 reflection, Angle eyeRotation, out Box2 maskBounds)
    {
        maskBounds = reflection;
        _masks.Clear();
        _puddles.Clear();
        var (_, _, inverseGrid) = _transform.GetWorldPositionRotationInvMatrix(grid);
        _lookup.GetLocalEntitiesIntersecting(grid, inverseGrid.TransformBox(reflection).Enlarged(0.15f), _puddles,
            LookupFlags.StaticSundries | LookupFlags.Sensors | LookupFlags.Approximate);
        foreach (var (uid, _) in _puddles)
        {
            if (!_entities.TryGetComponent(uid, out TransformComponent? xform) || !xform.Anchored ||
                !_entities.TryGetComponent(uid, out SpriteComponent? sprite) || !sprite.AddToTree || sprite.Color.A <= 0.01f ||
                !_sprites.TryGetLayer((uid, sprite), 0, out var layer, logMissing: false) || !layer.Visible || layer.Blank)
                continue;

            var (position, rotation) = _transform.GetWorldPositionRotation(xform);
            var screenAngle = (rotation + eyeRotation).Reduced().FlipPositive();
            if (GetLayerTexture(sprite, layer, screenAngle, out var direction) is not { } texture)
                continue;

            var transform = SpriteLayerWorldTransform.Get(sprite, layer, direction, position, rotation, screenAngle, eyeRotation);
            var bounds = GetTextureBounds(texture);
            var worldBounds = transform.TransformBox(bounds);
            if (!reflection.Intersects(worldBounds))
                continue;

            maskBounds = maskBounds.Union(worldBounds);
            _masks.Add(new PuddleMask(texture, transform, bounds));
        }

        return _masks.Count > 0;
    }

    private static Texture? GetLayerTexture(SpriteComponent sprite, SpriteComponent.Layer layer, Angle screenAngle,
        out RsiDirection direction)
    {
        direction = RsiDirection.South;
        if (layer.ActualState is not { } state)
            return layer.Texture;

        direction = SpriteComponent.Layer.GetDirection(state.RsiDirections, screenAngle);
        var frameDirection = SpriteLayerWorldTransform.GetFrameDirection(sprite, layer, state.RsiDirections, direction);
        return state.GetFrame(frameDirection, layer.AnimationFrame);
    }

    private static Box2 GetTextureBounds(Texture texture)
        => Box2.CenteredAround(Vector2.Zero, texture.Size / (float) EyeManager.PixelsPerMeter);

    protected override void DisposeBehavior()
    {
        _mask.Dispose();
        foreach (var shader in _drawShaders)
            shader.Dispose();
        _clearMask.Dispose();
        base.DisposeBehavior();
    }

    private readonly record struct PuddleMask(Texture Texture, Matrix3x2 Transform, Box2 Bounds);
}
