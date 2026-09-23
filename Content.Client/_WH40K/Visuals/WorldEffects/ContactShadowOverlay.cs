using System.Numerics;
using Content.Client.DamageState;
using Content.Shared.Buckle.Components;
using Content.Shared.Humanoid;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Stealth.Components;
using Content.Shared.Standing;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WH40K.Visuals.WorldEffects;

/// <summary>
/// Procedural contact shadows below mobs, below sprites and the normal FOV mask.
/// </summary>
public sealed partial class ContactShadowOverlay : Overlay
{
    private const int MaxShadows = 512;
    private static readonly ProtoId<ShaderPrototype> Shader = "WH40KContactShadow";

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private RsiVisualGeometryCache _geometry = default!;

    private readonly EntityLookupSystem _lookup;
    private readonly SharedTransformSystem _transform;
    private readonly TurfSystem _turf;
    private readonly ShaderInstance _shader;
    private readonly HashSet<EntityUid> _candidates = new();
    private readonly List<Shadow> _shadows = new();

    private static readonly HumanoidVisualLayers[] BodyLayers =
    [
        HumanoidVisualLayers.Chest,
        HumanoidVisualLayers.Head,
        HumanoidVisualLayers.LArm,
        HumanoidVisualLayers.RArm,
        HumanoidVisualLayers.LLeg,
        HumanoidVisualLayers.RLeg,
        HumanoidVisualLayers.LFoot,
        HumanoidVisualLayers.RFoot,
    ];

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public ContactShadowOverlay()
    {
        IoCManager.InjectDependencies(this);
        _lookup = _entities.System<EntityLookupSystem>();
        _transform = _entities.System<SharedTransformSystem>();
        _turf = _entities.System<TurfSystem>();
        _shader = _prototypes.Index(Shader).InstanceUnique();
        // Above floor details, below every mob draw depth.
        ZIndex = (int) DrawDepth.Puddles + 1;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace || args.Viewport.Eye is not { } eye)
            return;

        _candidates.Clear();
        _shadows.Clear();
        _lookup.GetEntitiesIntersecting(args.MapId, args.WorldAABB.Enlarged(1f), _candidates,
            LookupFlags.Uncontained | LookupFlags.Approximate);

        foreach (var uid in _candidates)
        {
            if (!_entities.TryGetComponent<SpriteComponent>(uid, out var sprite) ||
                !sprite.Visible || sprite.ContainerOccluded || sprite.Color.A <= 0.01f ||
                !_entities.TryGetComponent<TransformComponent>(uid, out var xform) ||
                xform.MapID != args.MapId ||
                (xform.ParentUid != xform.GridUid && xform.ParentUid != xform.MapUid) ||
                _entities.GetComponent<MetaDataComponent>(uid).VisibilityMask != 1 ||
                _entities.TryGetComponent<StealthComponent>(uid, out var stealth) && stealth.Enabled)
                continue;

            var isMob = _entities.HasComponent<MobStateComponent>(uid);
            if (!isMob)
                continue;

            if (_entities.TryGetComponent<BuckleComponent>(uid, out var buckle) && buckle.Buckled)
                continue;

            if (!_turf.TryGetTileRef(xform.Coordinates, out var tile) || _turf.IsSpace(tile.Value))
                continue;

            var lying = _entities.TryGetComponent<StandingStateComponent>(uid, out var standing) &&
                        standing.CurrentState is StandingState.Lying or StandingState.GettingUp;
            var position = _transform.GetWorldPosition(xform);
            var worldRotation = _transform.GetWorldRotation(xform);
            var spritePosition = position + (sprite.NoRotation
                ? (-eye.Rotation).RotateVec(sprite.Offset)
                : worldRotation.RotateVec(sprite.Offset));
            var spriteRotation = sprite.NoRotation
                ? sprite.Rotation - eye.Rotation
                : sprite.Rotation + worldRotation;

            var screenRight = (-eye.Rotation).RotateVec(Vector2.UnitX);
            var screenUp = (-eye.Rotation).RotateVec(Vector2.UnitY);
            var bodyAxis = lying ? spriteRotation.RotateVec(Vector2.UnitY) : screenRight;
            var crossAxis = lying ? new Vector2(bodyAxis.Y, -bodyAxis.X) : screenUp;
            var screenAngle = (worldRotation + eye.Rotation).Reduced().FlipPositive();
            var bodyBounds = new ContactShadowProjectedBounds();
            foreach (var key in BodyLayers)
            {
                if (sprite.LayerMapTryGet(key, out var index) && sprite[index] is SpriteComponent.Layer layer)
                    IncludeLayer(sprite, layer, position, worldRotation, screenAngle, eye.Rotation,
                        bodyAxis, crossAxis, ref bodyBounds, allowHidden: true);
            }

            // Simple mobs have one base layer instead of humanoid body-part layers.
            if (!bodyBounds.Valid && sprite.LayerMapTryGet(DamageStateVisualLayers.Base, out var baseIndex) &&
                sprite[baseIndex] is SpriteComponent.Layer baseLayer)
            {
                IncludeLayer(sprite, baseLayer, position, worldRotation, screenAngle, eye.Rotation,
                    bodyAxis, crossAxis, ref bodyBounds);
            }

            if (!bodyBounds.Valid)
            {
                foreach (var candidate in sprite.AllLayers)
                {
                    if (candidate is SpriteComponent.Layer layer &&
                        IncludeLayer(sprite, layer, position, worldRotation, screenAngle, eye.Rotation,
                            bodyAxis, crossAxis, ref bodyBounds))
                        break;
                }
            }

            // Direct textures and other sprites without RSI frame data retain a small shadow at their origin.
            if (!bodyBounds.Valid)
                bodyBounds.Include(Box2.CenteredAround(spritePosition, new Vector2(0.5f)),
                    Matrix3x2.Identity, bodyAxis, crossAxis);

            ContactShadowShape shape;
            if (lying)
            {
                shape = ContactShadowGeometry.Lying(bodyBounds, bodyAxis, crossAxis, screenUp,
                    spriteRotation + Angle.FromDegrees(90));
            }
            else
            {
                var footBounds = new ContactShadowProjectedBounds();
                // Visible footwear is the actual contact point when it covers the humanoid foot layers.
                if (sprite.LayerMapTryGet("shoes", out var shoes) &&
                    sprite[shoes] is SpriteComponent.Layer shoeLayer)
                {
                    IncludeLayer(sprite, shoeLayer, position, worldRotation, screenAngle, eye.Rotation,
                        screenRight, screenUp, ref footBounds);
                }

                if (!footBounds.Valid)
                {
                    if (sprite.LayerMapTryGet(HumanoidVisualLayers.LFoot, out var leftFoot) &&
                        sprite[leftFoot] is SpriteComponent.Layer leftLayer)
                    {
                        IncludeLayer(sprite, leftLayer, position, worldRotation, screenAngle, eye.Rotation,
                            screenRight, screenUp, ref footBounds, allowHidden: true);
                    }

                    if (sprite.LayerMapTryGet(HumanoidVisualLayers.RFoot, out var rightFoot) &&
                        sprite[rightFoot] is SpriteComponent.Layer rightLayer)
                    {
                        IncludeLayer(sprite, rightLayer, position, worldRotation, screenAngle, eye.Rotation,
                            screenRight, screenUp, ref footBounds, allowHidden: true);
                    }
                }

                shape = ContactShadowGeometry.Standing(footBounds, bodyBounds, screenRight, screenUp,
                    -eye.Rotation);
            }

            _shadows.Add(new Shadow(uid, shape.Center, shape.Size, shape.Rotation,
                sprite.Color.A * (lying ? 0.64f : 0.68f),
                Vector2.DistanceSquared(position, args.WorldAABB.Center)));
        }

        // Stable selection when a busy viewport exceeds the draw budget.
        if (_shadows.Count > MaxShadows)
        {
            _shadows.Sort(static (a, b) =>
            {
                var distance = a.Distance.CompareTo(b.Distance);
                return distance != 0 ? distance : a.Entity.CompareTo(b.Entity);
            });
        }

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        for (var i = 0; i < Math.Min(MaxShadows, _shadows.Count); i++)
        {
            var shadow = _shadows[i];
            // Keep one transform/shader for the whole batch of quads.
            var rect = new Box2Rotated(Box2.CenteredAround(shadow.Center, shadow.Size), shadow.Rotation, shadow.Center);
            handle.DrawRect(rect, Color.Black.WithAlpha(shadow.Alpha));
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        _shader.Dispose();
        base.DisposeBehavior();
    }

    private bool IncludeLayer(
        SpriteComponent sprite,
        SpriteComponent.Layer layer,
        Vector2 position,
        Angle worldRotation,
        Angle screenAngle,
        Angle eyeRotation,
        Vector2 axisX,
        Vector2 axisY,
        ref ContactShadowProjectedBounds bounds,
        bool allowHidden = false)
    {
        if ((!layer.Visible && !allowHidden) || layer.Blank || layer.Color.A <= 0.01f ||
            layer.CopyToShaderParameters != null ||
            layer.ActualState is not { } state)
            return false;

        var matrixDirection = SpriteComponent.Layer.GetDirection(state.RsiDirections, screenAngle);
        var textureDirection = sprite.EnableDirectionOverride
            ? sprite.DirectionOverride.Convert(state.RsiDirections)
            : matrixDirection;
        textureDirection = textureDirection.OffsetRsiDir(layer.DirOffset);
        var opaque = _geometry.GetOpaqueGeometry(state, textureDirection, layer.AnimationFrame);
        if (!opaque.Visible)
            return false;

        var transform = SpriteLayerWorldTransform.Get(sprite, layer, matrixDirection,
            position, worldRotation, screenAngle, eyeRotation);
        bounds.Include(opaque.Bounds, transform, axisX, axisY);
        return true;
    }

    private readonly record struct Shadow(EntityUid Entity, Vector2 Center, Vector2 Size, Angle Rotation, float Alpha, float Distance);
}
