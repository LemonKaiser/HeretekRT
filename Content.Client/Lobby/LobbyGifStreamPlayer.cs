using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Content.Client.Resources.Gif;
using Robust.Client.Graphics;
using Robust.Shared.Maths;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client.Lobby;

internal sealed class LobbyGifStreamPlayer : IDisposable
{
    private const int FrameQueueCapacity = 3;
    private const int TextureRingSize = 3;
    private const float MinFrameDelay = 0.01f;

    private readonly IClyde _clyde;
    private readonly object _sync = new();
    private readonly Queue<StreamedFrame> _frames = new();
    private readonly Stack<Rgba32[]> _recycledBuffers = new();
    private readonly CancellationTokenSource _cancellation = new();

    private Task? _decodeTask;
    private OwnedTexture[]? _textures;
    private Exception? _failure;
    private int _nextTextureIndex;
    private float _frameTimer;
    private bool _disposed;
    private bool _cancellationDisposed;
    private int _droppedFrames;
    private int _uploadedFrames;
    private double _uploadMilliseconds;

    public LobbyGifStreamPlayer(IClyde clyde)
    {
        _clyde = clyde;
    }

    public void Start(byte[] gifData)
    {
        if (_decodeTask != null)
            throw new InvalidOperationException("GIF stream has already started.");

        _decodeTask = Task.Run(() => DecodeLoop(gifData, _cancellation.Token));
    }

    public bool FrameUpdate(float deltaSeconds, out Texture? texture, out Exception? failure)
    {
        texture = null;
        failure = null;

        _frameTimer -= deltaSeconds;
        if (_frameTimer > 0f)
            return true;

        StreamedFrame? nextFrame = null;
        var overdueSeconds = MathF.Max(0f, -_frameTimer);
        lock (_sync)
        {
            while (_frames.Count > 1)
            {
                var skipped = _frames.Peek();
                var skippedDelay = MathF.Max(skipped.DelaySeconds, MinFrameDelay);
                if (overdueSeconds < skippedDelay)
                    break;

                _frames.Dequeue();
                _recycledBuffers.Push(skipped.Pixels);
                overdueSeconds -= skippedDelay;
                _droppedFrames++;
            }

            if (_frames.TryDequeue(out var frame))
            {
                nextFrame = frame;
                Monitor.PulseAll(_sync);
            }
            else if (_failure != null)
            {
                failure = _failure;
                return false;
            }
        }

        if (nextFrame == null)
            return true;

        var decodedFrame = nextFrame.Value;
        try
        {
            var target = GetNextTexture(decodedFrame.Width, decodedFrame.Height);
            var stopwatch = Stopwatch.StartNew();
            target.SetSubImage(Vector2i.Zero, new Vector2i(decodedFrame.Width, decodedFrame.Height), decodedFrame.Pixels);
            stopwatch.Stop();
            _frameTimer = MathF.Max(0f, MathF.Max(decodedFrame.DelaySeconds, MinFrameDelay) - overdueSeconds);
            lock (_sync)
            {
                _uploadedFrames++;
                _uploadMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
            }
            texture = target;
            return true;
        }
        finally
        {
            RecycleBuffer(decodedFrame.Pixels);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_sync)
        {
            _disposed = true;
            _cancellation.Cancel();
            _frames.Clear();
            _recycledBuffers.Clear();
            Monitor.PulseAll(_sync);
        }

        if (_textures != null)
        {
            foreach (var texture in _textures)
                texture.Dispose();

            _textures = null;
        }

        if (_decodeTask?.IsCompleted == true)
        {
            lock (_sync)
            {
                DisposeCancellationLocked();
            }
        }
    }

    public GifPlaybackMetrics GetMetrics()
    {
        lock (_sync)
        {
            var averageUploadMilliseconds = _uploadedFrames == 0
                ? 0d
                : _uploadMilliseconds / _uploadedFrames;
            return new GifPlaybackMetrics(_uploadedFrames, _droppedFrames, averageUploadMilliseconds);
        }
    }

    private void DecodeLoop(byte[] gifData, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var decoded = GifDecoder.DecodeFrames(
                    gifData,
                    GifDecoder.DecodeOptions.Default,
                    QueueFrame,
                    cancellationToken,
                    AcquireBuffer);

                if (decoded == 0)
                    throw new InvalidOperationException("GIF stream contains no frames.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            lock (_sync)
            {
                _failure = e;
                Monitor.PulseAll(_sync);
            }
        }
        finally
        {
            lock (_sync)
            {
                if (_disposed)
                    DisposeCancellationLocked();
            }
        }
    }

    private void QueueFrame(int width, int height, GifDecoder.StreamedFrame frame)
    {
        lock (_sync)
        {
            while (!_disposed && _frames.Count >= FrameQueueCapacity)
                Monitor.Wait(_sync);

            if (_disposed)
                return;

            _frames.Enqueue(new StreamedFrame(width, height, frame.Pixels, frame.DelaySeconds));
        }
    }

    private Rgba32[] AcquireBuffer(int pixelCount)
    {
        lock (_sync)
        {
            while (_recycledBuffers.TryPop(out var buffer))
            {
                if (buffer.Length == pixelCount)
                    return buffer;
            }
        }

        return new Rgba32[pixelCount];
    }

    private void RecycleBuffer(Rgba32[] buffer)
    {
        lock (_sync)
        {
            if (!_disposed)
                _recycledBuffers.Push(buffer);
        }
    }

    private OwnedTexture GetNextTexture(int width, int height)
    {
        if (_textures == null || _textures[0].Width != width || _textures[0].Height != height)
        {
            if (_textures != null)
            {
                foreach (var texture in _textures)
                    texture.Dispose();
            }

            _textures = new OwnedTexture[TextureRingSize];
            var size = new Vector2i(width, height);
            for (var i = 0; i < _textures.Length; i++)
                _textures[i] = _clyde.CreateBlankTexture<Rgba32>(size, $"lobby-gif-{i}");

            _nextTextureIndex = 0;
        }

        var result = _textures[_nextTextureIndex];
        _nextTextureIndex = (_nextTextureIndex + 1) % _textures.Length;
        return result;
    }

    private void DisposeCancellationLocked()
    {
        if (_cancellationDisposed)
            return;

        _cancellation.Dispose();
        _cancellationDisposed = true;
    }

    private readonly record struct StreamedFrame(int Width, int Height, Rgba32[] Pixels, float DelaySeconds);
    public readonly record struct GifPlaybackMetrics(int UploadedFrames, int DroppedFrames, double AverageUploadMilliseconds);
}
