using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

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
    {
        var startedAt = Stopwatch.GetTimestamp();
        var handle = args.WorldHandle;
        var mapId = args.MapId;
        var eyeAngle = (float) _eye.CurrentEye.Rotation;
        var culledParticles = 0;
        var drawnParticles = 0;
        var drawCalls = 0;

        ClearBuckets();
        BuildMaterialBuckets(args, mapId, ref culledParticles);
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

                AppendQuad(vertices, quadCount++, material.Uv, item.Emitter, particle, eyeAngle);
                drawnParticles++;
            }

            if (quadCount == 0)
                continue;

            DrawBatch(handle, material.Texture, quadCount, vertices);
            drawCalls++;
        }

        ClearBuckets();
        handle.UseShader(null);
        _system.ReportRenderStatistics(culledParticles, drawCalls, drawnParticles, ElapsedMilliseconds(startedAt));
    }

    private void BuildMaterialBuckets(in OverlayDrawArgs args, MapId mapId, ref int culledParticles)
    {
        foreach (var emitter in _system.GetEmitters())
        {
            if (emitter.MapCoords.MapId != mapId || emitter.LodMultiplier <= 0f || emitter.Frames.Length == 0)
                continue;

            var proto = emitter.Proto;
            var shader = string.IsNullOrEmpty(proto.Shader) ? null : proto.Shader;
            var layer = proto.RenderLayer;
            var particles = CollectionsMarshal.AsSpan(emitter.Particles);

            for (var index = 0; index < particles.Length; index++)
            {
                ref readonly var particle = ref particles[index];
                var worldPosition = ParticleSystem.GetParticleWorldPosition(particle, emitter);
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
        float eyeAngle)
    {
        var proto = emitter.Proto;
        var ageRatio = particle.AgeRatio;
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
        var velocitySquared = particle.Velocity.LengthSquared();
        if (stretchFactor > 0f && velocitySquared > 0.000001f)
        {
            var velocityLength = MathF.Sqrt(velocitySquared);
            halfY = halfSize * (1f + velocityLength * stretchFactor);
            cos = particle.Velocity.Y / velocityLength;
            sin = particle.Velocity.X / velocityLength;
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
        var worldPosition = ParticleSystem.GetParticleWorldPosition(particle, emitter);
        var vertex = quad * 4;

        vertices[vertex] = new DrawVertexUV2DColor(worldPosition + new Vector2(-halfXCos + halfYSin, -halfXSin - halfYCos), uv.BottomLeft, color) { UV2 = Uv2BL };
        vertices[vertex + 1] = new DrawVertexUV2DColor(worldPosition + new Vector2(halfXCos + halfYSin, halfXSin - halfYCos), uv.BottomRight, color) { UV2 = Uv2BR };
        vertices[vertex + 2] = new DrawVertexUV2DColor(worldPosition + new Vector2(halfXCos - halfYSin, halfXSin + halfYCos), uv.TopRight, color) { UV2 = Uv2TR };
        vertices[vertex + 3] = new DrawVertexUV2DColor(worldPosition + new Vector2(-halfXCos - halfYSin, -halfXSin + halfYCos), uv.TopLeft, color) { UV2 = Uv2TL };
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
}
