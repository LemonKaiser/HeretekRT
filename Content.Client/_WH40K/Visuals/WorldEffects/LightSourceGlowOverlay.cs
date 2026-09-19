using System.Numerics;
using Content.Client.Atmos.Components;
using Content.Client.Atmos.EntitySystems;
using Content.Shared.Light;
using Content.Shared.Physics;
using Content.Shared._WH40K.Visuals.WorldEffects;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._WH40K.Visuals.WorldEffects;

/// <summary>
/// Draws a compact emissive halo on actual light fixtures.
/// This deliberately does not imitate the light radius: illumination, masks and wall shadows remain the renderer's job.
/// </summary>
public sealed partial class LightSourceGlowOverlay : Overlay
{
    private const int MaxVisibleSources = 128;
    private const int HazeRayCount = 9;
    private const int RadialHazeRayCount = 24;
    private const float MountedRadialHazeArc = MathF.PI * 2f / 3f;
    private const float MountedSideBlockerMinDot = 0.342f; // cos(70 degrees), including collider margin.
    private static readonly TimeSpan HazeCacheLifetime = TimeSpan.FromSeconds(0.5);
    private static readonly ProtoId<ShaderPrototype> BloomShader = "WH40KLightSourceBloom";
    private static readonly ProtoId<ShaderPrototype> Shader = "WH40KLightSourceGlow";
    private static readonly ProtoId<ShaderPrototype> HazeShader = "WH40KDirectionalLightHaze";
    private static readonly ProtoId<ShaderPrototype> RadialHazeShader = "WH40KRadialLightHaze";

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private GlowGeometryCache _glowCache = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly EntityLookupSystem _lookup;
    private readonly SharedPhysicsSystem _physics;
    private readonly SharedTransformSystem _transform;
    private readonly ShaderInstance _bloomShader;
    private readonly ShaderInstance _shader;
    private readonly ShaderInstance _hazeShader;
    private readonly ShaderInstance _radialHazeShader;
    private readonly HashSet<EntityUid> _candidates = new();
    private readonly List<GlowSource> _sources = new();
    private readonly Dictionary<EntityUid, CachedHazeMesh> _hazeCache = new();
    private readonly Dictionary<EntityUid, CachedRadialHazeMesh> _radialHazeCache = new();
    private readonly List<EntityUid> _staleHazeCache = new();
    private readonly DrawVertexUV2DColor[] _hazeVertices = new DrawVertexUV2DColor[HazeRayCount * 2];
    private readonly DrawVertexUV2DColor[] _radialHazeVertices = new DrawVertexUV2DColor[RadialHazeRayCount + 2];
    private TimeSpan _nextCacheSweep;

    /// <summary>
    /// Set by <see cref="WorldVisualEffectsSystem"/> from the graphics options. Toggling this never recreates the
    /// overlay so that the glow geometry cache (owned by <see cref="GlowGeometryCache"/>) stays warm.
    /// </summary>
    public bool Enabled = true;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;
    public override bool RequestScreenTexture => true;

    public LightSourceGlowOverlay()
    {
        IoCManager.InjectDependencies(this);
        _lookup = _entities.System<EntityLookupSystem>();
        _physics = _entities.System<SharedPhysicsSystem>();
        _transform = _entities.System<SharedTransformSystem>();
        _bloomShader = _prototypes.Index(BloomShader).InstanceUnique();
        _shader = _prototypes.Index(Shader).InstanceUnique();
        _hazeShader = _prototypes.Index(HazeShader).InstanceUnique();
        _radialHazeShader = _prototypes.Index(RadialHazeShader).InstanceUnique();
        ZIndex = 1000;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return Enabled && args.MapId != MapId.Nullspace && args.Viewport.Eye != null;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not { } eye)
            return;

        _candidates.Clear();
        _sources.Clear();
        var queryBounds = args.WorldAABB.Enlarged(1f);
        _lookup.GetEntitiesIntersecting(args.MapId, queryBounds, _candidates,
            LookupFlags.Uncontained | LookupFlags.Approximate);

        // Fire lights live on sprite-less child entities and are not guaranteed to be present in the broadphase.
        var fireQuery = _entities.EntityQueryEnumerator<FireVisualsComponent, WH40KLightEffectsComponent, TransformComponent>();
        while (fireQuery.MoveNext(out _, out var fireVisuals, out _, out var fireTransform))
        {
            if (fireVisuals.LightEntity is { } fireLight &&
                fireTransform.MapID == args.MapId &&
                queryBounds.Contains(_transform.GetWorldPosition(fireTransform)))
            {
                _candidates.Add(fireLight);
            }
        }

        foreach (var uid in _candidates)
        {
            if (!_entities.TryGetComponent<PointLightComponent>(uid, out var light) ||
                !light.Enabled || light.ContainerOccluded || light.Energy <= 0f || light.Color.A <= 0.01f ||
                !_entities.TryGetComponent<TransformComponent>(uid, out var lightTransform) ||
                lightTransform.MapID != args.MapId)
                continue;

            var visualUid = uid;
            if (_entities.TryGetComponent<FireVisualsComponent>(lightTransform.ParentUid, out var fireVisuals) &&
                fireVisuals.LightEntity == uid)
            {
                visualUid = lightTransform.ParentUid;
            }

            if (!_entities.TryGetComponent<SpriteComponent>(visualUid, out var sprite) ||
                !sprite.Visible || sprite.ContainerOccluded || sprite.Color.A <= 0.01f ||
                !_entities.TryGetComponent<TransformComponent>(visualUid, out var transform) ||
                transform.MapID != args.MapId ||
                !_entities.TryGetComponent<WH40KLightEffectsComponent>(visualUid, out var effects))
                continue;

            var profile = effects.Profile;
            if (light.Radius < Math.Max(0f, effects.MinLightRadius ?? GetMinLightRadius(profile)) ||
                !TryGetEmitterLayer(sprite, effects, out var layerIndex) ||
                sprite[layerIndex] is not SpriteComponent.Layer { Visible: true } layer ||
                layer.Blank ||
                layer.CopyToShaderParameters != null)
                continue;

            var state = layer.ActualState;
            var (worldPosition, worldRotation) = _transform.GetWorldPositionRotation(transform);
            var angle = (worldRotation + eye.Rotation).Reduced().FlipPositive();
            var matrixDirection = state == null
                ? RsiDirection.South
                : SpriteComponent.Layer.GetDirection(state.RsiDirections, angle);
            var textureDirection = matrixDirection;

            if (sprite.EnableDirectionOverride && state != null)
                textureDirection = sprite.DirectionOverride.Convert(state.RsiDirections);

            textureDirection = textureDirection.OffsetRsiDir(layer.DirOffset);
            var geometry = _glowCache.GetGlowGeometry(state, textureDirection, layer.AnimationFrame);

            if (!geometry.Visible)
                continue;

            var transformMatrix = GetLayerTransform(sprite, layer, matrixDirection, worldPosition, worldRotation, angle, eye.Rotation);
            var center = Vector2.Transform(geometry.Center, transformMatrix);

            // Source glow and light haze are separate effects. Glow always surrounds the luminous pixels while only
            // haze is shaped and clipped by the fixture direction and nearby occluders.
            var energy = Math.Clamp(MathF.Sqrt(light.Energy), 0.65f, 1.35f);
            var sourceAlpha = light.Color.A * sprite.Color.A * layer.Color.A;
            var glowStrength = Math.Max(0f, effects.GlowStrength ?? GetGlowStrength(profile));
            var bloomStrength = Math.Max(0f, effects.BloomStrength ?? GetBloomStrength(profile));
            var glowOpacity = 0.19f * energy * sourceAlpha * glowStrength;
            var glowColor = Color.InterpolateBetween(light.Color, Color.White, 0.3f).WithAlpha(glowOpacity);
            var bloomOpacity = 0.64f * energy * sourceAlpha * bloomStrength;

            var outwardDirection = Vector2.Zero;
            if (state is { RsiDirections: not RsiDirectionType.Dir1 } && !sprite.NoRotation)
            {
                outwardDirection = Vector2.TransformNormal(GetLocalDirection(textureDirection), transformMatrix);
                if (outwardDirection.LengthSquared() > 0.001f)
                    outwardDirection = Vector2.Normalize(outwardDirection);
            }

            var hazeShape = light.MaskPath != null
                ? HazeShape.None
                : geometry.LinearEmitter && outwardDirection != Vector2.Zero
                    ? HazeShape.Directional
                    : outwardDirection != Vector2.Zero
                        ? HazeShape.RadialMounted
                        : HazeShape.RadialOmnidirectional;
            var hazeCenter = center;
            var hazeLength = Math.Clamp(light.Radius * 0.135f, 1.05f, 1.7f);
            var hazeHalfWidth = Math.Clamp(Math.Max(geometry.SourceSize.X, geometry.SourceSize.Y) * 0.62f, 0.2f, 0.48f);
            var radialHazeRadius = Math.Clamp(light.Radius * 0.18f, 1.15f, 1.75f);
            var hazeOpacity = 0.052f * energy * sourceAlpha;
            var effectScale = 1f;
            var hazeStrength = 1f;
            switch (profile)
            {
                case WH40KLightEffectProfile.Candle:
                    hazeShape = HazeShape.RadialOmnidirectional;
                    radialHazeRadius = Math.Clamp(light.Radius * 0.22f, 0.48f, 0.78f);
                    hazeStrength = 0.38f;
                    effectScale = 0.55f;
                    break;
                case WH40KLightEffectProfile.PortableLamp:
                    hazeLength = Math.Clamp(light.Radius * 0.22f, 0.72f, 1.08f);
                    hazeHalfWidth = Math.Clamp(hazeHalfWidth * 0.72f, 0.16f, 0.32f);
                    radialHazeRadius = Math.Clamp(light.Radius * 0.24f, 0.72f, 1.05f);
                    hazeStrength = 0.62f;
                    effectScale = 0.76f;
                    break;
                case WH40KLightEffectProfile.Floodlight:
                    hazeLength = Math.Clamp(light.Radius * 0.16f, 1f, 1.45f);
                    hazeHalfWidth = Math.Clamp(hazeHalfWidth * 0.9f, 0.22f, 0.44f);
                    radialHazeRadius = Math.Clamp(light.Radius * 0.18f, 1f, 1.45f);
                    hazeStrength = 0.82f;
                    effectScale = 0.88f;
                    break;
            }

            hazeShape = effects.Haze switch
            {
                WH40KLightHazeMode.None => HazeShape.None,
                WH40KLightHazeMode.Directional when outwardDirection != Vector2.Zero => HazeShape.Directional,
                WH40KLightHazeMode.Directional => HazeShape.None,
                WH40KLightHazeMode.RadialMounted when outwardDirection != Vector2.Zero => HazeShape.RadialMounted,
                WH40KLightHazeMode.RadialMounted => HazeShape.RadialOmnidirectional,
                WH40KLightHazeMode.RadialOmnidirectional => HazeShape.RadialOmnidirectional,
                _ => hazeShape,
            };
            hazeOpacity *= Math.Max(0f, effects.HazeStrength ?? hazeStrength);
            effectScale = Math.Max(0f, effects.EffectScale ?? effectScale);
            hazeLength *= Math.Max(0f, effects.HazeLengthScale ?? 1f);
            hazeHalfWidth *= Math.Max(0f, effects.HazeWidthScale ?? 1f);
            radialHazeRadius *= Math.Max(0f, effects.HazeRadiusScale ?? 1f);

            var hazeWhitening = geometry.LinearEmitter ? 0.14f : 0.2f;
            var hazeColor = Color.InterpolateBetween(light.Color, Color.White, hazeWhitening).WithAlpha(hazeOpacity);

            _sources.Add(new GlowSource(
                visualUid,
                transformMatrix,
                Box2.CenteredAround(geometry.Center, geometry.GlowSize * effectScale),
                Box2.CenteredAround(geometry.Center, geometry.BloomSize * effectScale),
                glowColor,
                bloomOpacity,
                hazeCenter,
                outwardDirection,
                hazeShape == HazeShape.Directional ? outwardDirection : Vector2.Zero,
                hazeLength,
                hazeHalfWidth,
                hazeShape,
                radialHazeRadius,
                hazeColor,
                Vector2.DistanceSquared(center, args.WorldAABB.Center)));
        }

        if (_sources.Count > MaxVisibleSources)
        {
            _sources.Sort(static (a, b) =>
            {
                var distance = a.Distance.CompareTo(b.Distance);
                return distance != 0 ? distance : a.Entity.CompareTo(b.Entity);
            });
        }

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        DrawRadialHaze(handle, args.MapId);
        DrawDirectionalHaze(handle, args.MapId);
        DrawSourceBloom(handle);
        DrawSourceGlow(handle);

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);

        SweepHazeCache();
    }

    private void DrawSourceBloom(DrawingHandleWorld handle)
    {
        if (ScreenTexture == null)
            return;

        // Bright-pass convolution reads the fixture as it appeared before this overlay. Haze is therefore never
        // sampled into bloom and cannot recursively brighten itself.
        _bloomShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        handle.UseShader(_bloomShader);

        for (var sourceIndex = 0; sourceIndex < Math.Min(MaxVisibleSources, _sources.Count); sourceIndex++)
        {
            var source = _sources[sourceIndex];
            handle.SetTransform(source.Transform);
            handle.DrawRect(source.BloomBounds, Color.White.WithAlpha(source.BloomOpacity));
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    private void DrawDirectionalHaze(DrawingHandleWorld handle, MapId mapId)
    {
        handle.UseShader(_hazeShader);

        for (var sourceIndex = 0; sourceIndex < Math.Min(MaxVisibleSources, _sources.Count); sourceIndex++)
        {
            var source = _sources[sourceIndex];
            if (source.HazeDirection == Vector2.Zero)
                continue;

            var mesh = GetHazeMesh(mapId, source);
            var color = Color.FromSrgb(source.HazeColor);
            for (var rayIndex = 0; rayIndex < HazeRayCount; rayIndex++)
            {
                var u = rayIndex / (HazeRayCount - 1f);
                _hazeVertices[rayIndex * 2] = new DrawVertexUV2DColor(mesh.Vertices[rayIndex * 2], new Vector2(0.5f), color)
                {
                    UV2 = new Vector2(u, 0f),
                };
                _hazeVertices[rayIndex * 2 + 1] = new DrawVertexUV2DColor(mesh.Vertices[rayIndex * 2 + 1], new Vector2(0.5f), color)
                {
                    UV2 = new Vector2(u, 1f),
                };
            }

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, Texture.White, _hazeVertices);
        }

        handle.UseShader(null);
    }

    private void DrawSourceGlow(DrawingHandleWorld handle)
    {
        handle.UseShader(_shader);

        for (var sourceIndex = 0; sourceIndex < Math.Min(MaxVisibleSources, _sources.Count); sourceIndex++)
        {
            var source = _sources[sourceIndex];
            handle.SetTransform(source.Transform);
            handle.DrawRect(source.GlowBounds, source.GlowColor);
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    private void DrawRadialHaze(DrawingHandleWorld handle, MapId mapId)
    {
        handle.UseShader(_radialHazeShader);

        for (var sourceIndex = 0; sourceIndex < Math.Min(MaxVisibleSources, _sources.Count); sourceIndex++)
        {
            var source = _sources[sourceIndex];
            if (source.HazeShape is not (HazeShape.RadialMounted or HazeShape.RadialOmnidirectional))
                continue;

            var mesh = GetRadialHazeMesh(mapId, source);
            var color = Color.FromSrgb(source.HazeColor);
            var isMounted = source.HazeShape == HazeShape.RadialMounted;
            var shaderMode = new Vector2(isMounted ? 1f : 0f, 0f);
            _radialHazeVertices[0] = new DrawVertexUV2DColor(mesh.Vertices[0], shaderMode, color)
            {
                UV2 = new Vector2(0.5f),
            };

            for (var rayIndex = 0; rayIndex <= RadialHazeRayCount; rayIndex++)
            {
                var radialUv = GetRadialDirection(source.MountDirection, rayIndex, RadialHazeRayCount);
                var shaderDirection = radialUv;
                if (isMounted)
                {
                    var perpendicular = new Vector2(-source.MountDirection.Y, source.MountDirection.X);
                    shaderDirection = new Vector2(
                        Vector2.Dot(radialUv, perpendicular),
                        Vector2.Dot(radialUv, source.MountDirection));
                }

                _radialHazeVertices[rayIndex + 1] = new DrawVertexUV2DColor(
                    mesh.Vertices[rayIndex + 1],
                    shaderMode,
                    color)
                {
                    UV2 = new Vector2(0.5f) + shaderDirection * 0.5f,
                };
            }

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, Texture.White, _radialHazeVertices);
        }

        handle.UseShader(null);
    }

    private CachedHazeMesh GetHazeMesh(MapId mapId, in GlowSource source)
    {
        var now = _timing.CurTime;
        if (_hazeCache.TryGetValue(source.Entity, out var cached) &&
            cached.ExpiresAt > now &&
            Vector2.DistanceSquared(cached.Center, source.HazeCenter) < 0.0004f &&
            Vector2.Dot(cached.Direction, source.HazeDirection) > 0.999f &&
            MathHelper.CloseTo(cached.Length, source.HazeLength) &&
            MathHelper.CloseTo(cached.HalfWidth, source.HazeHalfWidth))
        {
            return cached;
        }

        var positions = new Vector2[HazeRayCount * 2];
        var perpendicular = new Vector2(-source.HazeDirection.Y, source.HazeDirection.X);

        for (var rayIndex = 0; rayIndex < HazeRayCount; rayIndex++)
        {
            var across = rayIndex / (HazeRayCount - 1f) * 2f - 1f;
            var start = source.HazeCenter + perpendicular * source.HazeHalfWidth * across;
            var rayDirection = Vector2.Normalize(source.HazeDirection + perpendicular * across * 0.13f);
            var distance = GetUnoccludedHazeLength(
                mapId,
                source.Entity,
                source.HazeCenter,
                start,
                rayDirection,
                source.MountDirection,
                false,
                source.HazeLength);

            positions[rayIndex * 2] = start;
            positions[rayIndex * 2 + 1] = start + rayDirection * distance;
        }

        cached = new CachedHazeMesh(
            source.HazeCenter,
            source.HazeDirection,
            source.HazeLength,
            source.HazeHalfWidth,
            now + HazeCacheLifetime,
            positions);
        _hazeCache[source.Entity] = cached;
        return cached;
    }

    private CachedRadialHazeMesh GetRadialHazeMesh(MapId mapId, in GlowSource source)
    {
        var now = _timing.CurTime;
        if (_radialHazeCache.TryGetValue(source.Entity, out var cached) &&
            cached.ExpiresAt > now &&
            Vector2.DistanceSquared(cached.Center, source.HazeCenter) < 0.0004f &&
            cached.MountDirection == source.MountDirection &&
            MathHelper.CloseTo(cached.Radius, source.RadialHazeRadius))
        {
            return cached;
        }

        var positions = new Vector2[RadialHazeRayCount + 2];
        positions[0] = source.HazeCenter;
        for (var rayIndex = 0; rayIndex <= RadialHazeRayCount; rayIndex++)
        {
            var direction = GetRadialDirection(source.MountDirection, rayIndex, RadialHazeRayCount);
            var distance = GetUnoccludedHazeLength(
                mapId,
                source.Entity,
                source.HazeCenter,
                source.HazeCenter,
                direction,
                source.MountDirection,
                source.HazeShape == HazeShape.RadialMounted,
                source.RadialHazeRadius);
            positions[rayIndex + 1] = source.HazeCenter + direction * distance;
        }

        cached = new CachedRadialHazeMesh(
            source.HazeCenter,
            source.MountDirection,
            source.RadialHazeRadius,
            now + HazeCacheLifetime,
            positions);
        _radialHazeCache[source.Entity] = cached;
        return cached;
    }

    private float GetUnoccludedHazeLength(
        MapId mapId,
        EntityUid source,
        Vector2 emitterCenter,
        Vector2 start,
        Vector2 direction,
        Vector2 mountDirection,
        bool ignoreAdjacentSideBlockers,
        float maximumLength)
    {
        var ray = new CollisionRay(start, direction, (int) CollisionGroup.Opaque);
        var state = (
            Entities: _entities,
            Transform: _transform,
            Source: source,
            EmitterCenter: emitterCenter,
            Direction: direction,
            MountDirection: mountDirection,
            IgnoreAdjacentSideBlockers: ignoreAdjacentSideBlockers);
        var results = _physics.IntersectRayWithPredicate(
            mapId,
            ray,
            state,
            static (uid, context) =>
            {
                if (uid == context.Source ||
                    !context.Entities.TryGetComponent<OccluderComponent>(uid, out var occluder) ||
                    !occluder.Enabled)
                {
                    return true;
                }

                // Ignore only the nearby wall the fixture is mounted in, and only for the outward hemisphere.
                if (!context.Entities.TryGetComponent<TransformComponent>(uid, out var blockerTransform))
                    return false;

                var blockerOffset = context.Transform.GetWorldPosition(blockerTransform) - context.EmitterCenter;

                // A neighbouring door or wall can have a collider extending into an edge ray despite its center
                // being outside the mounted fixture's light sector. Ignore only such nearby side neighbours.
                if (context.IgnoreAdjacentSideBlockers &&
                    blockerOffset.LengthSquared() is > 0.0001f and <= 1.5625f &&
                    Vector2.Dot(Vector2.Normalize(blockerOffset), context.MountDirection) < MountedSideBlockerMinDot)
                {
                    return true;
                }

                return blockerOffset.LengthSquared() <= 0.5625f &&
                       context.MountDirection != Vector2.Zero &&
                       Vector2.Dot(blockerOffset, context.MountDirection) <= 0.05f &&
                       Vector2.Dot(context.Direction, context.MountDirection) >= -0.01f;
            },
            maximumLength,
            true);

        foreach (var result in results)
            return Math.Clamp(result.Distance - 0.04f, 0.08f, maximumLength);

        return maximumLength;
    }

    private static Vector2 GetRadialDirection(Vector2 mountDirection, int rayIndex, int rayCount)
    {
        if (mountDirection == Vector2.Zero)
        {
            var angle = MathF.Tau * rayIndex / rayCount;
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        var halfCircleAngle = -MountedRadialHazeArc / 2f + MountedRadialHazeArc * rayIndex / rayCount;
        var perpendicular = new Vector2(-mountDirection.Y, mountDirection.X);
        return mountDirection * MathF.Cos(halfCircleAngle) + perpendicular * MathF.Sin(halfCircleAngle);
    }

    private static bool TryGetEmitterLayer(
        SpriteComponent sprite,
        WH40KLightEffectsComponent effects,
        out int layerIndex)
    {
        switch (effects.EmitterLayer)
        {
            case WH40KLightEmitterLayer.PoweredGlow:
                return sprite.LayerMapTryGet(PoweredLightLayers.Glow, out layerIndex);
            case WH40KLightEmitterLayer.ToggleableLight:
                return sprite.LayerMapTryGet("light", out layerIndex);
            case WH40KLightEmitterLayer.Fire:
                if (sprite.LayerMapTryGet(FireVisualLayers.Fire, out layerIndex))
                    return true;
                return TryGetTopmostUnshadedLayer(sprite, out layerIndex);
            case WH40KLightEmitterLayer.TopmostUnshaded:
                return TryGetTopmostUnshadedLayer(sprite, out layerIndex);
            case WH40KLightEmitterLayer.Custom when effects.CustomLayer != null:
                return sprite.LayerMapTryGet(effects.CustomLayer, out layerIndex);
            default:
                layerIndex = default;
                return false;
        }
    }

    private static bool TryGetTopmostUnshadedLayer(SpriteComponent sprite, out int layerIndex)
    {
        var index = 0;
        var found = false;
        layerIndex = default;
        foreach (var spriteLayer in sprite.AllLayers)
        {
            if (spriteLayer is SpriteComponent.Layer { Visible: true, Blank: false } layer &&
                layer.ShaderPrototype == "unshaded")
            {
                layerIndex = index;
                found = true;
            }

            index++;
        }

        return found;
    }

    private static float GetGlowStrength(WH40KLightEffectProfile profile)
    {
        return profile switch
        {
            WH40KLightEffectProfile.Candle => 0.34f,
            WH40KLightEffectProfile.PortableLamp => 0.58f,
            WH40KLightEffectProfile.Floodlight => 0.78f,
            _ => 1f,
        };
    }

    private static float GetBloomStrength(WH40KLightEffectProfile profile)
    {
        return profile switch
        {
            WH40KLightEffectProfile.Candle => 0.3f,
            WH40KLightEffectProfile.PortableLamp => 0.55f,
            WH40KLightEffectProfile.Floodlight => 0.76f,
            _ => 1f,
        };
    }

    private static float GetMinLightRadius(WH40KLightEffectProfile profile)
    {
        return profile == WH40KLightEffectProfile.Fixture ? 3f : 0.75f;
    }

    private void SweepHazeCache()
    {
        if (_timing.CurTime < _nextCacheSweep)
            return;

        _nextCacheSweep = _timing.CurTime + TimeSpan.FromSeconds(5);
        _staleHazeCache.Clear();
        foreach (var uid in _hazeCache.Keys)
        {
            if (!_entities.EntityExists(uid))
                _staleHazeCache.Add(uid);
        }

        foreach (var uid in _radialHazeCache.Keys)
        {
            if (!_entities.EntityExists(uid))
                _staleHazeCache.Add(uid);
        }

        foreach (var uid in _staleHazeCache)
        {
            _hazeCache.Remove(uid);
            _radialHazeCache.Remove(uid);
        }
    }

    private static Vector2 GetLocalDirection(RsiDirection direction)
    {
        return direction switch
        {
            RsiDirection.North => Vector2.UnitY,
            RsiDirection.East => Vector2.UnitX,
            RsiDirection.West => -Vector2.UnitX,
            RsiDirection.SouthEast => Vector2.Normalize(new Vector2(1f, -1f)),
            RsiDirection.SouthWest => Vector2.Normalize(new Vector2(-1f, -1f)),
            RsiDirection.NorthEast => Vector2.Normalize(new Vector2(1f, 1f)),
            RsiDirection.NorthWest => Vector2.Normalize(new Vector2(-1f, 1f)),
            _ => -Vector2.UnitY,
        };
    }

    private static Matrix3x2 GetLayerTransform(
        SpriteComponent sprite,
        SpriteComponent.Layer layer,
        RsiDirection direction,
        Vector2 worldPosition,
        Angle worldRotation,
        Angle screenAngle,
        Angle eyeRotation)
    {
        var cardinal = Angle.Zero;
        if (!sprite.NoRotation && sprite.SnapCardinals)
            cardinal = screenAngle.RoundToCardinalAngle();

        var entityMatrix = Matrix3Helpers.CreateTransform(
            worldPosition,
            sprite.NoRotation ? -eyeRotation : worldRotation - cardinal);
        var spriteMatrix = Matrix3x2.Multiply(sprite.LocalMatrix, entityMatrix);

        if (sprite.GranularLayersRendering)
        {
            entityMatrix = layer.RenderingStrategy switch
            {
                LayerRenderingStrategy.Default =>
                    Matrix3Helpers.CreateTransform(worldPosition, worldRotation),
                LayerRenderingStrategy.NoRotation =>
                    Matrix3Helpers.CreateTransform(worldPosition, -eyeRotation),
                LayerRenderingStrategy.SnapToCardinals =>
                    Matrix3Helpers.CreateTransform(worldPosition, worldRotation - screenAngle.RoundToCardinalAngle()),
                _ => entityMatrix,
            };
            spriteMatrix = Matrix3x2.Multiply(sprite.LocalMatrix, entityMatrix);
        }

        layer.GetLayerDrawMatrix(direction, out var layerMatrix);
        return Matrix3x2.Multiply(layerMatrix, spriteMatrix);
    }

    protected override void DisposeBehavior()
    {
        _radialHazeShader.Dispose();
        _hazeShader.Dispose();
        _shader.Dispose();
        _bloomShader.Dispose();
        base.DisposeBehavior();
    }

    private readonly record struct GlowSource(
        EntityUid Entity,
        Matrix3x2 Transform,
        Box2 GlowBounds,
        Box2 BloomBounds,
        Color GlowColor,
        float BloomOpacity,
        Vector2 HazeCenter,
        Vector2 MountDirection,
        Vector2 HazeDirection,
        float HazeLength,
        float HazeHalfWidth,
        HazeShape HazeShape,
        float RadialHazeRadius,
        Color HazeColor,
        float Distance);

    private enum HazeShape : byte
    {
        None,
        Directional,
        RadialMounted,
        RadialOmnidirectional,
    }

    private sealed record CachedHazeMesh(
        Vector2 Center,
        Vector2 Direction,
        float Length,
        float HalfWidth,
        TimeSpan ExpiresAt,
        Vector2[] Vertices);

    private sealed record CachedRadialHazeMesh(
        Vector2 Center,
        Vector2 MountDirection,
        float Radius,
        TimeSpan ExpiresAt,
        Vector2[] Vertices);
}
