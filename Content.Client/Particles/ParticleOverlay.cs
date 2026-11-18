using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client.Particles;

/// <summary>
/// Draws live particles after their world-space position has been resolved by <see cref="ParticleSystem"/>.
/// Particles sharing a material are batched even when they belong to different emitters.
/// </summary>
public sealed class ParticleOverlay : Overlay
{
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly ParticleSystem _system;
    private readonly Dictionary<string, ShaderInstance?> _shaderCache = new();
    private readonly Dictionary<Texture, (Texture Source, Box2 Uv)> _atlasCache = new();
    private readonly Dictionary<ParticleMaterialKey, List<ParticleRenderItem>> _materialBuckets = new();
    private readonly List<ParticleMaterialKey> _activeMaterials = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private const int MaxQuadsPerDraw = 16383;
    private static readonly Vector2 Uv2BL = new(0f, 0f);
    private static readonly Vector2 Uv2BR = new(1f, 0f);
    private static readonly Vector2 Uv2TR = new(1f, 1f);
    private static readonly Vector2 Uv2TL = new(0f, 1f);

    private readonly DrawVertexUV2DColor[] _vertexScratch = new DrawVertexUV2DColor[MaxQuadsPerDraw * 4];
    private readonly ushort[] _indexScratch;
    private (long Viewport, int Culled, int DrawCalls, int Drawn, float Milliseconds) _groundStatistics;

    private readonly record struct ParticleMaterialKey(int RenderLayer, string? Shader, Texture Texture, Box2 Uv);
    private readonly record struct ParticleRenderItem(ActiveEmitter Emitter, int ParticleIndex);

    public ParticleOverlay(ParticleSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        _indexScratch = BuildQuadIndices(MaxQuadsPerDraw);
    }

    /// <summary>
    /// Resources backing a particle prototype may change during hot reload.
    /// The next draw must then resolve shaders and atlas regions again.
    /// </summary>
    internal void ClearCaches()
    {
        ClearBuckets();
        _materialBuckets.Clear();
        _atlasCache.Clear();
        _shaderCache.Clear();
    }

    private static ushort[] BuildQuadIndices(int quadCount)
    {
        var indices = new ushort[quadCount * 6];
        for (var quad = 0; quad < quadCount; quad++)
        {
            var vertex = (ushort) (quad * 4);
            var index = quad * 6;
            indices[index] = vertex;
            indices[index + 1] = (ushort) (vertex + 1);
            indices[index + 2] = (ushort) (vertex + 2);
            indices[index + 3] = vertex;
            indices[index + 4] = (ushort) (vertex + 2);
            indices[index + 5] = (ushort) (vertex + 3);
        }

        return indices;
    }

    private (Texture Source, Box2 Uv) ResolveAtlasCached(Texture texture)
    {
        if (_atlasCache.TryGetValue(texture, out var cached))
            return cached;

        var resolved = ResolveAtlas(texture);
        _atlasCache.Add(texture, resolved);
        return resolved;
    }

    private static (Texture Source, Box2 Uv) ResolveAtlas(Texture texture)
    {
        if (texture is not AtlasTexture atlas)
            return (texture, new Box2(0f, 0f, 1f, 1f));

        var width = (float) atlas.SourceTexture.Width;
        var height = (float) atlas.SourceTexture.Height;
        var region = atlas.SubRegion;
        return (atlas.SourceTexture, new Box2(
            region.Left / width,
            (height - region.Bottom) / height,
            region.Right / width,
            (height - region.Top) / height));
    }

    protected override void Draw(in OverlayDrawArgs args)
        => DrawParticles(args, groundedPass: false);

    private void DrawParticles(in OverlayDrawArgs args, bool groundedPass)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var handle = args.WorldHandle;
        var mapId = args.MapId;
        var eyeAngle = (float) _eye.CurrentEye.Rotation;
        var viewportAngle = (float) (args.Viewport.Eye ?? _eye.CurrentEye).Rotation;
        var screenUp = new Vector2(MathF.Sin(viewportAngle), MathF.Cos(viewportAngle));
        var culledParticles = 0;
        var drawnParticles = 0;
        var drawCalls = 0;

        ClearBuckets();
        BuildMaterialBuckets(args, mapId, screenUp, groundedPass, ref culledParticles);
        _activeMaterials.Sort(MaterialComparison);

        foreach (var material in _activeMaterials)
        {
            UseShader(handle, material.Shader);
            var quadCount = 0;
            var vertices = _vertexScratch.AsSpan();
            var items = _materialBuckets[material];

            foreach (var item in items)
            {
                var particles = CollectionsMarshal.AsSpan(item.Emitter.Particles);
                ref readonly var particle = ref particles[item.ParticleIndex];
                if (quadCount >= MaxQuadsPerDraw)
                {
                    DrawBatch(handle, material.Texture, quadCount, vertices);
                    drawCalls++;
                    quadCount = 0;
                }

                AppendQuad(vertices, quadCount++, material.Uv, item.Emitter, particle, eyeAngle, screenUp);
                drawnParticles++;
            }

            if (quadCount == 0)
                continue;

            DrawBatch(handle, material.Texture, quadCount, vertices);
            drawCalls++;
        }

        ClearBuckets();
        handle.UseShader(null);
        var elapsed = ElapsedMilliseconds(startedAt);
        if (groundedPass)
        {
            _groundStatistics = (args.Viewport.Id, culledParticles, drawCalls, drawnParticles, elapsed);
            return;
        }

        var ground = _groundStatistics.Viewport == args.Viewport.Id ? _groundStatistics : default;
        _groundStatistics = default;
        _system.ReportRenderStatistics(culledParticles + ground.Culled, drawCalls + ground.DrawCalls,
            drawnParticles + ground.Drawn, elapsed + ground.Milliseconds);
    }

    private void BuildMaterialBuckets(in OverlayDrawArgs args, MapId mapId, Vector2 screenUp, bool groundedPass,
        ref int culledParticles)
    {
        foreach (var emitter in _system.GetEmitters())
        {
            if (emitter.MapCoords.MapId != mapId || emitter.LodMultiplier <= 0f || emitter.Frames.Length == 0 ||
                groundedPass && emitter.Proto.Flight == null)
                continue;

            var proto = emitter.Proto;
            var shader = groundedPass ? proto.Flight?.GroundShader ?? proto.Shader : proto.Shader;
            if (string.IsNullOrEmpty(shader))
                shader = null;
            var layer = proto.RenderLayer;
            var particles = CollectionsMarshal.AsSpan(emitter.Particles);

            for (var index = 0; index < particles.Length; index++)
            {
                ref readonly var particle = ref particles[index];
                if (particle.Age < 0f || particle.Grounded != groundedPass)
                    continue;

                var worldPosition = GetDrawPosition(particle, emitter, screenUp);
                if (!args.WorldBounds.Contains(worldPosition))
                {
                    culledParticles++;
                    continue;
                }

                var rawTexture = emitter.Frames[GetFrameIndex(emitter, particle)];
                var (texture, uv) = ResolveAtlasCached(rawTexture);
                var key = new ParticleMaterialKey(layer, shader, texture, uv);
                if (!_materialBuckets.TryGetValue(key, out var items))
                {
                    items = new List<ParticleRenderItem>();
                    _materialBuckets.Add(key, items);
                }

                if (items.Count == 0)
                    _activeMaterials.Add(key);
                items.Add(new ParticleRenderItem(emitter, index));
            }
        }
    }

    private static Vector2 GetDrawPosition(in ParticleData particle, ActiveEmitter emitter, Vector2 screenUp)
    {
        var position = ParticleSystem.GetParticleWorldPosition(particle, emitter);
        if (particle.FlightDuration <= 0f || particle.Grounded)
            return position;

        var age = particle.Age;
        var height = particle.LaunchHeight + age * (particle.LaunchLift - 0.5f * particle.FallAcceleration * age);
        return position + screenUp * Math.Max(height, 0f);
    }

    private static int GetFrameIndex(ActiveEmitter emitter, in ParticleData particle)
    {
        if (emitter.Frames.Length <= 1 || emitter.Delays.Length == 0 || emitter.AnimationDuration <= 0f)
            return 0;

        var time = (emitter.AnimationTime + particle.AnimationPhase) % emitter.AnimationDuration;
        var frameCount = Math.Min(emitter.Frames.Length, emitter.Delays.Length);
        for (var index = 0; index < frameCount; index++)
        {
            var delay = Math.Max(0f, emitter.Delays[index]);
            if (time < delay)
                return index;
            time -= delay;
        }

        return 0;
    }

    private static int MaterialComparison(ParticleMaterialKey left, ParticleMaterialKey right)
    {
        var comparison = left.RenderLayer.CompareTo(right.RenderLayer);
        if (comparison != 0)
            return comparison;

        comparison = string.CompareOrdinal(left.Shader, right.Shader);
        if (comparison != 0)
            return comparison;

        comparison = left.Texture.GetHashCode().CompareTo(right.Texture.GetHashCode());
        return comparison != 0 ? comparison : left.Uv.GetHashCode().CompareTo(right.Uv.GetHashCode());
    }

    private void UseShader(DrawingHandleWorld handle, string? shader)
    {
        if (shader == null)
        {
            handle.UseShader(null);
            return;
        }

        if (!_shaderCache.TryGetValue(shader, out var cached))
        {
            if (_proto.Resolve<ShaderPrototype>(shader, out var shaderProto))
                cached = shaderProto.Instance();
            _shaderCache.Add(shader, cached);
        }

        handle.UseShader(cached);
    }

    private void AppendQuad(
        Span<DrawVertexUV2DColor> vertices,
        int quad,
        Box2 uv,
        ActiveEmitter emitter,
        in ParticleData particle,
        float eyeAngle,
        Vector2 screenUp)
    {
        var proto = emitter.Proto;
        var ageRatio = particle.AgeRatio;
        if (proto.ShaderAnimation)
        {
            var parameters = new Vector2(ageRatio, (particle.Variation + 0.5f) / 256f);
            uv = new Box2(parameters, parameters);
        }

        var color = CompiledParticleEffect.Sample(
            emitter.Runtime.ColorOverLifetime,
            ageRatio,
            Color.InterpolateBetween(proto.StartColor, proto.EndColor, ageRatio));
        var tintColor = emitter.ColorOverride;
        if (tintColor is { } tint)
            color = new Color(color.R * tint.R, color.G * tint.G, color.B * tint.B, color.A * tint.A);

        color = color.WithAlpha(color.A * CompiledParticleEffect.Sample(emitter.Runtime.AlphaOverLifetime, ageRatio, 1f));
        var halfSize = proto.ParticleSize * 0.5f * particle.SpawnIntensity * particle.SizeMultiplier
            * CompiledParticleEffect.Sample(emitter.Runtime.SizeOverLifetime, ageRatio, 1f);
        var stretchFactor = proto.StretchFactor;

        float halfX = halfSize;
        float halfY = halfSize;
        float cos;
        float sin;
        var velocity = particle.Velocity;
        if (particle.FlightDuration > 0f && !particle.Grounded)
        {
            var flightProgress = particle.Age / particle.FlightDuration;
            var verticalSpeed = particle.LaunchLift - particle.FallAcceleration * particle.Age;
            velocity += screenUp * verticalSpeed;
            var streak = (particle.Variation & 3) == 0 ? 1f : 0.36f;
            stretchFactor *= streak * (1f - 0.78f * flightProgress);
        }

        var velocitySquared = velocity.LengthSquared();
        var variant = Vector2.Zero;
        if (particle.Grounded)
        {
            var groundAge = particle.Age - particle.FlightDuration;
            var surfaceTint = 0.86f - 0.20f * Math.Clamp(groundAge / 1.3f, 0f, 1f);
            color = new Color(color.R * surfaceTint, color.G * surfaceTint, color.B * surfaceTint, color.A);
            var spread = Math.Clamp(groundAge * 22f, 0f, 1f);
            halfX *= 1f + 0.58f * spread;
            halfY *= 1f - 0.34f * spread;
            var direction = particle.GroundSlide != Vector2.Zero && velocitySquared > 0.000001f
                ? velocity / MathF.Sqrt(velocitySquared)
                : new Vector2(screenUp.Y, -screenUp.X);
            cos = direction.X;
            sin = direction.Y;
            // Store a stable shape index alongside the local UV while keeping all marks in one material batch.
            if (!string.IsNullOrEmpty(proto.Flight?.GroundShader))
                variant.X = 2f * (particle.Variation % 6);
        }
        else if (stretchFactor > 0f && velocitySquared > 0.000001f)
        {
            var velocityLength = MathF.Sqrt(velocitySquared);
            halfY = halfSize * (1f + velocityLength * stretchFactor);
            cos = velocity.Y / velocityLength;
            sin = (particle.FlightDuration > 0f ? -velocity.X : velocity.X) / velocityLength;
        }
        else if (proto.AlignToEmission)
        {
            cos = MathF.Cos(emitter.EffectiveEmitAngle);
            sin = -MathF.Sin(emitter.EffectiveEmitAngle);
        }
        else
        {
            var rotation = -eyeAngle + particle.Rotation;
            cos = MathF.Cos(rotation);
            sin = MathF.Sin(rotation);
        }

        var halfXCos = halfX * cos;
        var halfXSin = halfX * sin;
        var halfYCos = halfY * cos;
        var halfYSin = halfY * sin;
        var worldPosition = GetDrawPosition(particle, emitter, screenUp);
        var vertex = quad * 4;

        vertices[vertex] = new DrawVertexUV2DColor(worldPosition + new Vector2(-halfXCos + halfYSin, -halfXSin - halfYCos), uv.BottomLeft, color) { UV2 = Uv2BL + variant };
        vertices[vertex + 1] = new DrawVertexUV2DColor(worldPosition + new Vector2(halfXCos + halfYSin, halfXSin - halfYCos), uv.BottomRight, color) { UV2 = Uv2BR + variant };
        vertices[vertex + 2] = new DrawVertexUV2DColor(worldPosition + new Vector2(halfXCos - halfYSin, halfXSin + halfYCos), uv.TopRight, color) { UV2 = Uv2TR + variant };
        vertices[vertex + 3] = new DrawVertexUV2DColor(worldPosition + new Vector2(-halfXCos - halfYSin, -halfXSin + halfYCos), uv.TopLeft, color) { UV2 = Uv2TL + variant };
    }

    private void DrawBatch(DrawingHandleWorld handle, Texture texture, int quadCount, Span<DrawVertexUV2DColor> vertices)
    {
        handle.DrawPrimitives(
            DrawPrimitiveTopology.TriangleList,
            texture,
            _indexScratch.AsSpan(0, quadCount * 6),
            vertices.Slice(0, quadCount * 4));
    }

    private void ClearBuckets()
    {
        foreach (var material in _activeMaterials)
            _materialBuckets[material].Clear();
        _activeMaterials.Clear();
    }

    private static float ElapsedMilliseconds(long startedAt)
        => (Stopwatch.GetTimestamp() - startedAt) * 1000f / Stopwatch.Frequency;

    internal sealed class GroundOverlay : Overlay
    {
        private readonly ParticleOverlay _particles;
        public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

        public GroundOverlay(ParticleOverlay particles)
        {
            _particles = particles;
            ZIndex = (int) DrawDepth.Puddles + 3;
        }

        protected override bool BeforeDraw(in OverlayDrawArgs args) => _particles._system.HasGroundedParticles;

        protected override void Draw(in OverlayDrawArgs args) => _particles.DrawParticles(args, groundedPass: true);
    }
}
