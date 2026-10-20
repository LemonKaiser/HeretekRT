using System.Numerics;
using Content.Client._WH40K.Visuals.WorldEffects;
using Content.Client.Clickable;
using Content.Shared._WH40K.Administration;
using Content.Shared.Damage.Components;
using Content.Shared.Humanoid;
using Content.Shared.Eye;
using Content.Shared.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WH40K.Administration;

public partial class UndeadEyeOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "WH40KUndeadEye";
    private static readonly Box2 FlameBounds = new(-0.36f, -0.12f, 0.36f, 0.50f);

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private RsiVisualGeometryCache _geometry = default!;
    [Dependency] private IClickMapManager _pixels = default!;

    private readonly EntityLookupSystem _lookup;
    private readonly SharedTransformSystem _transform;
    private readonly SpriteSystem _sprites;
    private readonly UndeadEyeFrameCache _eyes;
    private readonly bool _behindCharacter;
    private readonly HashSet<EntityUid> _candidates = new();
    private readonly Dictionary<EntityUid, ShaderInstance> _shaders = new();
    private readonly List<EntityUid> _removed = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public UndeadEyeOverlay() : this(false)
    {
    }

    protected UndeadEyeOverlay(bool behindCharacter)
    {
        IoCManager.InjectDependencies(this);
        _lookup = _entities.System<EntityLookupSystem>();
        _transform = _entities.System<SharedTransformSystem>();
        _sprites = _entities.System<SpriteSystem>();
        _eyes = new UndeadEyeFrameCache(_pixels, _geometry);
        _behindCharacter = behindCharacter;
        // The far eye's flame must be covered by the actual body, hair and equipment,
        // including when the character uses a lower mob draw depth while prone.
        ZIndex = behindCharacter ? (int) DrawDepth.DeadMobs - 1 : (int) DrawDepth.OverMobs;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace || args.Viewport.Eye is not { } eye)
            return;

        RemoveInactiveShaders();
        _candidates.Clear();
        _lookup.GetEntitiesIntersecting(args.MapId, args.WorldAABB.Enlarged(1f), _candidates,
            LookupFlags.Uncontained | LookupFlags.Approximate);
        var handle = args.WorldHandle;

        foreach (var uid in _candidates)
        {
            var godmode = _entities.HasComponent<GodmodeComponent>(uid);
            if ((!godmode && !_entities.HasComponent<UndeadComponent>(uid))
                || !_entities.TryGetComponent<SpriteComponent>(uid, out var sprite)
                || !sprite.Visible || sprite.ContainerOccluded || sprite.Color.A <= 0.01f
                || !_entities.TryGetComponent<TransformComponent>(uid, out var xform)
                || xform.MapID != args.MapId
                || (xform.ParentUid != xform.MapUid && xform.ParentUid != xform.GridUid)
                || (_entities.GetComponent<MetaDataComponent>(uid).VisibilityMask & (int) VisibilityFlags.Normal) == 0
                || _entities.TryGetComponent<StealthComponent>(uid, out var stealth) && stealth.Enabled
                || !_entities.TryGetComponent<HumanoidAppearanceComponent>(uid, out var humanoid)
                || humanoid.PermanentlyHidden.Contains(HumanoidVisualLayers.Head)
                || !_sprites.LayerMapTryGet((uid, sprite), HumanoidVisualLayers.Head, out var headIndex, false)
                || sprite[headIndex] is not SpriteComponent.Layer headLayer)
            {
                continue;
            }

            var worldRotation = _transform.GetWorldRotation(xform);
            var screenAngle = (worldRotation + eye.Rotation).Reduced().FlipPositive();
            var facing = sprite.EnableDirectionOverride
                ? sprite.DirectionOverride.Convert(RsiDirectionType.Dir4)
                : SpriteComponent.Layer.GetDirection(RsiDirectionType.Dir4, screenAngle);
            var head = GetGeometry(sprite, headLayer, screenAngle);
            var layer = headLayer;
            var currentEyes = default(EyeFrameGeometry);
            var frontEyes = default(EyeFrameGeometry);
            var rightEyes = default(EyeFrameGeometry);

            if (_sprites.LayerMapTryGet((uid, sprite), HumanoidVisualLayers.Eyes, out var eyeIndex, false)
                && sprite[eyeIndex] is SpriteComponent.Layer eyeLayer && eyeLayer.ActualState is { } eyeState)
            {
                layer = eyeLayer;
                var textureDirection = sprite.EnableDirectionOverride
                    ? sprite.DirectionOverride.Convert(eyeState.RsiDirections)
                    : SpriteComponent.Layer.GetDirection(eyeState.RsiDirections, screenAngle);
                textureDirection = textureDirection.OffsetRsiDir(eyeLayer.DirOffset);
                currentEyes = _eyes.Get(eyeState, textureDirection, eyeLayer.AnimationFrame, head);
                frontEyes = _eyes.Get(eyeState, RsiDirection.South, eyeLayer.AnimationFrame, head);
                rightEyes = _eyes.Get(eyeState, RsiDirection.East, eyeLayer.AnimationFrame, head);
                facing = facing.OffsetRsiDir(eyeLayer.DirOffset);
            }

            var anchor = UndeadEyeGeometry.Resolve(facing, currentEyes, frontEyes, rightEyes, head);
            if (!anchor.Visible || anchor.BehindCharacter != _behindCharacter || layer.ActualState is not { } state)
                continue;

            // Eyeless visors use head geometry and its transform rather than an empty eye layer.
            if (!currentEyes.Visible && !frontEyes.Visible && !rightEyes.Visible)
                layer = headLayer;

            state = layer.ActualState!;
            var direction = SpriteComponent.Layer.GetDirection(state.RsiDirections, screenAngle);
            var layerTransform = SpriteLayerWorldTransform.Get(sprite, layer, direction,
                _transform.GetWorldPosition(xform), worldRotation, screenAngle, eye.Rotation);
            var anchorPosition = Vector2.Transform(anchor.Position, layerTransform);
            var effectScale = new Vector2(
                new Vector2(layerTransform.M11, layerTransform.M12).Length(),
                new Vector2(layerTransform.M21, layerTransform.M22).Length()) * anchor.Scale;
            // The source follows the head when prone; the flame still rises toward the top of the viewport.
            var effectTransform = Matrix3x2.CreateScale(effectScale)
                * Matrix3x2.CreateRotation((float) -eye.Rotation)
                * Matrix3x2.CreateTranslation(anchorPosition);

            if (!_shaders.TryGetValue(uid, out var shader))
            {
                shader = _prototypes.Index(Shader).InstanceUnique();
                shader.SetParameter("seed", Math.Abs(uid.GetHashCode() % 997) * 0.173f);
                _shaders.Add(uid, shader);
            }

            shader.SetParameter("godmode", godmode ? 1f : 0f);
            shader.SetParameter("trailDirection", anchor.TrailDirection);
            shader.SetParameter("coreVisibility", anchor.CoreVisibility);
            handle.SetTransform(effectTransform);
            handle.UseShader(shader);
            handle.DrawRect(FlameBounds, Color.White.WithAlpha(sprite.Color.A));
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    private OpaqueGeometry GetGeometry(SpriteComponent sprite, SpriteComponent.Layer layer, Angle screenAngle)
    {
        if (layer.ActualState is not { } state)
            return default;

        var direction = sprite.EnableDirectionOverride
            ? sprite.DirectionOverride.Convert(state.RsiDirections)
            : SpriteComponent.Layer.GetDirection(state.RsiDirections, screenAngle);
        direction = direction.OffsetRsiDir(layer.DirOffset);
        return _geometry.GetOpaqueGeometry(state, direction, layer.AnimationFrame);
    }

    private void RemoveInactiveShaders()
    {
        _removed.Clear();
        foreach (var (uid, _) in _shaders)
        {
            if (!_entities.HasComponent<UndeadComponent>(uid)
                && !_entities.HasComponent<GodmodeComponent>(uid))
                _removed.Add(uid);
        }

        foreach (var uid in _removed)
        {
            _shaders[uid].Dispose();
            _shaders.Remove(uid);
        }
    }

    protected override void DisposeBehavior()
    {
        foreach (var shader in _shaders.Values)
            shader.Dispose();
        _shaders.Clear();
        base.DisposeBehavior();
    }
}

public sealed class UndeadEyeBehindOverlay : UndeadEyeOverlay
{
    public UndeadEyeBehindOverlay() : base(true)
    {
    }
}
