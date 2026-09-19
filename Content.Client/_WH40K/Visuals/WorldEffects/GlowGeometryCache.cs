using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.Utility;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.IoC;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._WH40K.Visuals.WorldEffects;

/// <summary>
/// Caches the luminous-pixel geometry of every loaded RSI, so the light-source glow overlay can draw its halos.
/// Subscribes to <see cref="IResourceCache.OnRsiLoaded"/> in <see cref="PostInject"/>: this runs before the
/// engine's RSI preload, so light fixtures loaded at startup are retained. A late-subscribing EntitySystem
/// (created during EntityManager.Initialize) would silently miss them.
/// Follows the same pattern as <see cref="Content.Client.Clickable.ClickMapManager"/>.
/// </summary>
public sealed partial class GlowGeometryCache : IPostInjectInit
{
    [Dependency] private readonly IResourceCache _resources = default!;

    private readonly Dictionary<RSI, Dictionary<RSI.StateId, Dictionary<RsiDirection, GlowGeometry[]>>> _geometry =
        new();

    public void PostInject()
    {
        _resources.OnRsiLoaded += OnRsiLoaded;
    }

    public GlowGeometry GetGlowGeometry(RSI.State? state, RsiDirection direction, int animationFrame)
    {
        if (state == null)
            return default;

        if (!_geometry.TryGetValue(state.RSI, out var stateCache))
            return default;

        if (!stateCache.TryGetValue(state.StateId, out var directionCache))
            return default;

        if (!directionCache.TryGetValue(direction, out var frames) || frames.Length == 0)
            return default;

        return frames[animationFrame % frames.Length];
    }

    private void OnRsiLoaded(RsiLoadedEventArgs args)
    {
        if (args.Atlas is not Image<Rgba32> atlas)
            return;

        foreach (var (stateId, directionOffsets) in args.AtlasOffsets)
        {
            var directionCache = new Dictionary<RsiDirection, GlowGeometry[]>();
            for (var direction = 0; direction < directionOffsets.Length; direction++)
            {
                var frameOffsets = directionOffsets[direction];
                if (frameOffsets.Length == 0)
                    continue;

                var frames = new GlowGeometry[frameOffsets.Length];
                for (var frame = 0; frame < frameOffsets.Length; frame++)
                {
                    var offset = frameOffsets[frame];
                    frames[frame] = AnalyzeFrame(
                        atlas,
                        offset.X,
                        offset.Y,
                        args.Resource.RSI.Size.X,
                        args.Resource.RSI.Size.Y);
                }

                directionCache[(RsiDirection) direction] = frames;
            }

            if (directionCache.Count == 0)
                continue;

            if (!_geometry.TryGetValue(args.Resource.RSI, out var stateCache))
            {
                stateCache = new Dictionary<RSI.StateId, Dictionary<RsiDirection, GlowGeometry[]>>();
                _geometry.Add(args.Resource.RSI, stateCache);
            }

            stateCache.Add(stateId, directionCache);
        }
    }

    private static GlowGeometry AnalyzeFrame(
        Image<Rgba32> image,
        int frameX,
        int frameY,
        int frameWidth,
        int frameHeight)
    {
        var minX = frameWidth;
        var minY = frameHeight;
        var maxX = -1;
        var maxY = -1;
        var weightedX = 0f;
        var weightedY = 0f;
        var totalWeight = 0f;

        // The sandbox does not allow Image<Rgba32> indexers, so read the raw pixel span
        // the same way ClickMapManager does.
        var pixels = image.GetPixelSpan();
        var imageWidth = image.Width;

        for (var y = 0; y < frameHeight; y++)
        {
            for (var x = 0; x < frameWidth; x++)
            {
                var pixel = pixels[(frameY + y) * imageWidth + frameX + x];
                var weight = pixel.A / 255f * Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) / 255f;
                if (weight <= 0.01f)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                weightedX += (x + 0.5f) * weight;
                weightedY += (y + 0.5f) * weight;
                totalWeight += weight;
            }
        }

        if (totalWeight <= 0f)
            return default;

        var pixelCenter = new Vector2(weightedX, weightedY) / totalWeight;
        var center = new Vector2(
            pixelCenter.X - frameWidth / 2f,
            frameHeight / 2f - pixelCenter.Y) / EyeManager.PixelsPerMeter;
        var pixelWidth = maxX - minX + 1;
        var pixelHeight = maxY - minY + 1;
        var shortAxis = Math.Min(pixelWidth, pixelHeight);
        var linearEmitter = shortAxis > 0 && Math.Max(pixelWidth, pixelHeight) / (float) shortAxis >= 2f;
        var sourceSize = new Vector2(pixelWidth, pixelHeight) / EyeManager.PixelsPerMeter;
        Vector2 glowSize;
        Vector2 bloomSize;
        if (linearEmitter)
        {
            glowSize = Vector2.Clamp(
                sourceSize + new Vector2(0.5f, 0.42f),
                new Vector2(0.55f, 0.35f),
                new Vector2(1.35f, 0.78f));
            bloomSize = Vector2.Clamp(
                glowSize + new Vector2(0.28f, 0.22f),
                new Vector2(0.75f, 0.55f),
                new Vector2(1.55f, 1f));
        }
        else
        {
            // Round lamps must remain round even if their luminous sprite has an asymmetric pixel bounding box.
            var diameter = Math.Clamp(Math.Max(sourceSize.X, sourceSize.Y) + 0.48f, 0.52f, 0.82f);
            var bloomDiameter = Math.Clamp(diameter + 0.28f, 0.72f, 1.08f);
            glowSize = new Vector2(diameter);
            bloomSize = new Vector2(bloomDiameter);
        }

        return new GlowGeometry(true, center, sourceSize, glowSize, bloomSize, linearEmitter);
    }
}

/// <summary>Bounding geometry of the luminous pixels of one RSI frame.</summary>
public readonly record struct GlowGeometry(
    bool Visible,
    Vector2 Center,
    Vector2 SourceSize,
    Vector2 GlowSize,
    Vector2 BloomSize,
    bool LinearEmitter);