using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client.Resources.Gif;

public static class GifDecoder
{
    private const int GifMaxCodeSize = 4096;
    private const int DefaultFrameSafetyLimit = 10000;
    private const int DefaultCanvasPixelLimit = 4 * 1024 * 1024;
    private const float DefaultGifFrameDelay = 0.1f;
    private const float MinGifFrameDelay = 0.01f;

    private const byte GifExtension = 0x21;
    private const byte GifImageDescriptor = 0x2C;
    private const byte GifTrailer = 0x3B;
    private const byte GifGraphicControlExtension = 0xF9;

    public readonly record struct DecodedAnimation(int Width, int Height, DecodedFrame[] Frames);
    public readonly record struct DecodedFrame(byte[] Pixels, float DelaySeconds);
    public readonly record struct StreamedFrame(Rgba32[] Pixels, float DelaySeconds);

    public readonly record struct DecodeOptions(
        int MaxFrameCount,
        bool StopAtFrameLimit,
        float DefaultFrameDelaySeconds,
        float MinFrameDelaySeconds,
        int MaxCanvasPixels)
    {
        public static DecodeOptions Default => new(
            DefaultFrameSafetyLimit,
            StopAtFrameLimit: false,
            DefaultGifFrameDelay,
            MinGifFrameDelay,
            DefaultCanvasPixelLimit);

        public static DecodeOptions FirstFrameOnly => new(
            MaxFrameCount: 1,
            StopAtFrameLimit: true,
            DefaultGifFrameDelay,
            MinGifFrameDelay,
            DefaultCanvasPixelLimit);
    }

    public static DecodedAnimation Decode(byte[] gifData, CancellationToken cancellationToken = default)
    {
        return Decode(gifData, DecodeOptions.Default, cancellationToken);
    }

    public static DecodedAnimation Decode(
        byte[] gifData,
        DecodeOptions options,
        CancellationToken cancellationToken = default)
    {
        if (gifData.Length == 0)
            return new DecodedAnimation(0, 0, Array.Empty<DecodedFrame>());

        using var stream = new MemoryStream(gifData, writable: false);
        return Decode(stream, options, cancellationToken);
    }

    public static DecodedAnimation Decode(
        ReadOnlyMemory<byte> gifData,
        CancellationToken cancellationToken = default)
    {
        return Decode(gifData, DecodeOptions.Default, cancellationToken);
    }

    public static DecodedAnimation Decode(
        ReadOnlyMemory<byte> gifData,
        DecodeOptions options,
        CancellationToken cancellationToken = default)
    {
        if (gifData.Length == 0)
            return new DecodedAnimation(0, 0, Array.Empty<DecodedFrame>());

        using var stream = new MemoryStream(gifData.ToArray(), writable: false);
        return Decode(stream, options, cancellationToken);
    }

    public static DecodedAnimation DecodeFirstFrame(
        ReadOnlyMemory<byte> gifData,
        CancellationToken cancellationToken = default)
    {
        return Decode(gifData, DecodeOptions.FirstFrameOnly, cancellationToken);
    }

    public static DecodedAnimation DecodeFirstFrame(byte[] gifData, CancellationToken cancellationToken = default)
    {
        return Decode(gifData, DecodeOptions.FirstFrameOnly, cancellationToken);
    }

    public static DecodedAnimation Decode(
        Stream stream,
        DecodeOptions options,
        CancellationToken cancellationToken = default)
    {
        var width = 0;
        var height = 0;
        var frames = new List<DecodedFrame>();
        DecodeFrames(stream, options, (frameWidth, frameHeight, frame) =>
        {
            width = frameWidth;
            height = frameHeight;
            frames.Add(new DecodedFrame(ToBytes(frame.Pixels), frame.DelaySeconds));
        }, cancellationToken);

        return new DecodedAnimation(width, height, frames.ToArray());
    }

    public static DecodedAnimation DecodeFirstFrame(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        return Decode(stream, DecodeOptions.FirstFrameOnly, cancellationToken);
    }

    public static int DecodeFrames(
        byte[] gifData,
        DecodeOptions options,
        Action<int, int, StreamedFrame> onFrame,
        CancellationToken cancellationToken = default,
        Func<int, Rgba32[]>? frameBufferFactory = null)
    {
        if (gifData.Length == 0)
            return 0;

        using var stream = new MemoryStream(gifData, writable: false);
        return DecodeFrames(stream, options, onFrame, cancellationToken, frameBufferFactory);
    }

    public static int DecodeFrames(
        ReadOnlyMemory<byte> gifData,
        DecodeOptions options,
        Action<int, int, StreamedFrame> onFrame,
        CancellationToken cancellationToken = default,
        Func<int, Rgba32[]>? frameBufferFactory = null)
    {
        if (gifData.Length == 0)
            return 0;

        using var stream = new MemoryStream(gifData.ToArray(), writable: false);
        return DecodeFrames(stream, options, onFrame, cancellationToken, frameBufferFactory);
    }

    public static int DecodeFrames(
        Stream stream,
        DecodeOptions options,
        Action<int, int, StreamedFrame> onFrame,
        CancellationToken cancellationToken = default,
        Func<int, Rgba32[]>? frameBufferFactory = null)
    {
        return DecodeRaw(stream, options, onFrame, cancellationToken, frameBufferFactory);
    }

    private static int DecodeRaw(
        Stream stream,
        DecodeOptions options,
        Action<int, int, StreamedFrame> onFrame,
        CancellationToken cancellationToken,
        Func<int, Rgba32[]>? frameBufferFactory)
    {
        if (options.MaxFrameCount <= 0)
            return 0;

        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        var signature = reader.ReadBytes(6);
        if (signature.Length != 6
            || signature[0] != (byte) 'G'
            || signature[1] != (byte) 'I'
            || signature[2] != (byte) 'F')
        {
            throw new InvalidDataException("Invalid GIF signature.");
        }

        var screenWidth = reader.ReadUInt16();
        var screenHeight = reader.ReadUInt16();
        if (screenWidth <= 0 || screenHeight <= 0)
            throw new InvalidDataException("Invalid GIF logical screen size.");

        var canvasPixelCount = (long) screenWidth * screenHeight;
        if (canvasPixelCount > options.MaxCanvasPixels)
            throw new InvalidDataException($"GIF canvas exceeds the pixel limit ({canvasPixelCount} > {options.MaxCanvasPixels}).");

        var canvasPixels = (int) canvasPixelCount;

        var packed = reader.ReadByte();
        var hasGlobalColorTable = (packed & 0x80) != 0;
        var globalColorTableSize = 1 << ((packed & 0x07) + 1);

        _ = reader.ReadByte();
        _ = reader.ReadByte();

        Rgba32[]? globalColorTable = null;
        if (hasGlobalColorTable)
            globalColorTable = ReadColorTable(reader, globalColorTableSize);

        var canvas = new Rgba32[canvasPixels];
        var lzwScratch = new GifLzwScratch();
        var frameCount = 0;
        var gce = GraphicControlExtension.Default;
        PreviousFrameState? previousFrame = null;
        Rgba32[]? restoreBuffer = null;

        while (TryReadByte(reader, out var blockId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (blockId)
            {
                case GifExtension:
                    if (!TryReadByte(reader, out var extensionLabel))
                        throw new InvalidDataException("Unexpected EOF while reading GIF extension.");

                    if (extensionLabel == GifGraphicControlExtension)
                        gce = ReadGraphicControlExtension(reader);
                    else
                        SkipSubBlocks(reader);
                    break;

                case GifImageDescriptor:
                {
                    if (frameCount >= options.MaxFrameCount)
                    {
                        if (options.StopAtFrameLimit)
                            return frameCount;

                        throw new InvalidDataException(
                            $"GIF contains too many frames ({frameCount + 1}). Limit is {options.MaxFrameCount}.");
                    }

                    ApplyDisposal(canvas, screenWidth, screenHeight, previousFrame);

                    var left = reader.ReadUInt16();
                    var top = reader.ReadUInt16();
                    var width = reader.ReadUInt16();
                    var height = reader.ReadUInt16();
                    if (width <= 0 || height <= 0)
                        throw new InvalidDataException("Invalid GIF frame size.");

                    var imagePacked = reader.ReadByte();
                    var hasLocalColorTable = (imagePacked & 0x80) != 0;
                    var interlaced = (imagePacked & 0x40) != 0;
                    var localColorTableSize = 1 << ((imagePacked & 0x07) + 1);

                    var colorTable = hasLocalColorTable
                        ? ReadColorTable(reader, localColorTableSize)
                        : globalColorTable;

                    if (colorTable == null)
                        throw new InvalidDataException("GIF frame has no color table.");

                    var framePixelCount = (long) width * height;
                    if (framePixelCount > options.MaxCanvasPixels)
                    {
                        throw new InvalidDataException(
                            $"GIF frame exceeds the pixel limit ({framePixelCount} > {options.MaxCanvasPixels}).");
                    }

                    var lzwMinCodeSize = reader.ReadByte();
                    Rgba32[]? restoreSnapshot = null;
                    if (gce.DisposalMethod == 3)
                    {
                        restoreBuffer ??= new Rgba32[canvas.Length];
                        Array.Copy(canvas, restoreBuffer, canvas.Length);
                        restoreSnapshot = restoreBuffer;
                    }

                    var expectedPixels = (int) framePixelCount;
                    lzwScratch.EnsureColorCapacity(expectedPixels);
                    var compressedLength = ReadSubBlocks(reader, lzwScratch);
                    DecodeLzwFrame(lzwMinCodeSize, lzwScratch.CompressedData, compressedLength, expectedPixels, lzwScratch, cancellationToken);
                    DrawFrame(
                        canvas,
                        screenWidth,
                        screenHeight,
                        left,
                        top,
                        width,
                        height,
                        interlaced,
                        lzwScratch.ColorIndices,
                        colorTable,
                        gce.TransparentColorFlag,
                        gce.TransparentColorIndex,
                        cancellationToken);

                    var framePixels = frameBufferFactory?.Invoke(canvas.Length) ?? new Rgba32[canvas.Length];
                    if (framePixels.Length != canvas.Length)
                        throw new InvalidDataException("GIF frame buffer has an invalid size.");

                    Array.Copy(canvas, framePixels, canvas.Length);
                    var delay = gce.DelayCentiseconds > 0
                        ? gce.DelayCentiseconds / 100f
                        : options.DefaultFrameDelaySeconds;
                    onFrame(screenWidth, screenHeight, new StreamedFrame(
                        framePixels,
                        MathF.Max(delay, options.MinFrameDelaySeconds)));
                    frameCount++;

                    previousFrame = new PreviousFrameState(
                        left,
                        top,
                        width,
                        height,
                        gce.DisposalMethod,
                        restoreSnapshot);

                    gce = GraphicControlExtension.Default;
                    break;
                }

                case GifTrailer:
                    return frameCount;

                default:
                    throw new InvalidDataException($"Unexpected GIF block id 0x{blockId:X2}.");
            }
        }

        throw new InvalidDataException("Unexpected EOF before GIF trailer.");
    }

    private static void DecodeLzwFrame(
        int minCodeSize,
        byte[] compressedData,
        int compressedLength,
        int expectedPixels,
        GifLzwScratch scratch,
        CancellationToken cancellationToken)
    {
        if (minCodeSize <= 0 || minCodeSize > 8)
            throw new InvalidDataException($"Unsupported GIF LZW minimum code size: {minCodeSize}");

        var clearCode = 1 << minCodeSize;
        var endCode = clearCode + 1;
        var nextCode = clearCode + 2;
        var codeSize = minCodeSize + 1;
        var codeMask = (1 << codeSize) - 1;
        var datum = 0;
        var bits = 0;
        var oldCode = -1;
        var first = 0;
        var stackTop = 0;
        var outputCount = 0;
        var dataIndex = 0;

        for (var i = 0; i < clearCode; i++)
            scratch.Suffix[i] = (byte) i;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (bits < codeSize)
            {
                if (dataIndex >= compressedLength)
                {
                    if (outputCount == expectedPixels)
                        return;

                    throw new InvalidDataException(
                        $"Unexpected EOF in GIF LZW data after {outputCount} of {expectedPixels} pixels.");
                }

                var nextByte = compressedData[dataIndex++];
                datum |= nextByte << bits;
                bits += 8;
            }

            var code = datum & codeMask;
            datum >>= codeSize;
            bits -= codeSize;

            if (code == clearCode)
            {
                codeSize = minCodeSize + 1;
                codeMask = (1 << codeSize) - 1;
                nextCode = clearCode + 2;
                oldCode = -1;
                continue;
            }

            if (code == endCode)
                break;

            if (oldCode == -1)
            {
                if (code >= clearCode)
                    throw new InvalidDataException("Invalid first GIF LZW code.");

                if (outputCount >= expectedPixels)
                    throw new InvalidDataException("GIF LZW frame contains too many pixels.");

                scratch.ColorIndices[outputCount++] = (byte) code;
                first = code;
                oldCode = code;
                continue;
            }

            var inputCode = code;
            if (code == nextCode)
            {
                if (stackTop >= scratch.PixelStack.Length)
                    throw new InvalidDataException("GIF LZW stack overflow.");

                scratch.PixelStack[stackTop++] = (byte) first;
                code = oldCode;
            }
            else if (code > nextCode)
            {
                throw new InvalidDataException("Invalid GIF LZW code.");
            }

            while (code > clearCode)
            {
                if (code >= nextCode || stackTop >= scratch.PixelStack.Length)
                    throw new InvalidDataException("Invalid GIF LZW dictionary reference.");

                scratch.PixelStack[stackTop++] = scratch.Suffix[code];
                code = scratch.Prefix[code];
            }

            first = scratch.Suffix[code];
            if (stackTop >= scratch.PixelStack.Length)
                throw new InvalidDataException("GIF LZW stack overflow.");

            scratch.PixelStack[stackTop++] = (byte) first;

            while (stackTop > 0)
            {
                if (outputCount >= expectedPixels)
                    throw new InvalidDataException("GIF LZW frame contains too many pixels.");

                scratch.ColorIndices[outputCount++] = scratch.PixelStack[--stackTop];
            }

            if (nextCode < GifMaxCodeSize)
            {
                scratch.Prefix[nextCode] = (short) oldCode;
                scratch.Suffix[nextCode] = (byte) first;
                nextCode++;

                if (nextCode == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                    codeMask = (1 << codeSize) - 1;
                }
            }

            oldCode = inputCode;
        }

        if (outputCount != expectedPixels)
            throw new InvalidDataException("GIF LZW frame ended before all pixels were decoded.");

    }

    private static void DrawFrame(
        Rgba32[] canvas,
        int screenWidth,
        int screenHeight,
        int left,
        int top,
        int frameWidth,
        int frameHeight,
        bool interlaced,
        byte[] colorIndices,
        Rgba32[] colorTable,
        bool hasTransparency,
        byte transparentIndex,
        CancellationToken cancellationToken)
    {
        for (var dataRow = 0; dataRow < frameHeight; dataRow++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var screenY = top + (interlaced ? GetInterlacedRow(dataRow, frameHeight) : dataRow);
            if (screenY < 0 || screenY >= screenHeight)
                continue;

            var rowOffset = dataRow * frameWidth;
            for (var x = 0; x < frameWidth; x++)
            {
                var screenX = left + x;
                if (screenX < 0 || screenX >= screenWidth)
                    continue;

                var colorIndex = colorIndices[rowOffset + x];
                if (hasTransparency && colorIndex == transparentIndex)
                    continue;

                if (colorIndex >= colorTable.Length)
                    throw new InvalidDataException("GIF frame references a missing color table entry.");

                canvas[(screenY * screenWidth) + screenX] = colorTable[colorIndex];
            }
        }
    }

    private static int GetInterlacedRow(int index, int height)
    {
        var firstPass = (height + 7) / 8;
        if (index < firstPass)
            return index * 8;

        index -= firstPass;
        var secondPass = height <= 4 ? 0 : ((height - 5) / 8) + 1;
        if (index < secondPass)
            return 4 + index * 8;

        index -= secondPass;
        var thirdPass = height <= 2 ? 0 : ((height - 3) / 4) + 1;
        if (index < thirdPass)
            return 2 + index * 4;

        index -= thirdPass;
        return 1 + index * 2;
    }

    private static void ApplyDisposal(
        Rgba32[] canvas,
        int screenWidth,
        int screenHeight,
        PreviousFrameState? previous)
    {
        if (previous == null)
            return;

        switch (previous.Value.DisposalMethod)
        {
            case 2:
                ClearRect(
                    canvas,
                    screenWidth,
                    screenHeight,
                    previous.Value.Left,
                    previous.Value.Top,
                    previous.Value.Width,
                    previous.Value.Height);
                break;
            case 3:
                if (previous.Value.RestoreSnapshot != null)
                    Array.Copy(previous.Value.RestoreSnapshot, canvas, canvas.Length);
                break;
        }
    }

    private static void ClearRect(
        Rgba32[] canvas,
        int screenWidth,
        int screenHeight,
        int left,
        int top,
        int width,
        int height)
    {
        var startX = Math.Max(left, 0);
        var startY = Math.Max(top, 0);
        var endX = Math.Min(left + width, screenWidth);
        var endY = Math.Min(top + height, screenHeight);
        var rowLength = endX - startX;

        if (rowLength <= 0 || endY <= startY)
            return;

        var span = canvas.AsSpan();
        for (var y = startY; y < endY; y++)
            span.Slice((y * screenWidth) + startX, rowLength).Clear();
    }

    private static Rgba32[] ReadColorTable(BinaryReader reader, int size)
    {
        var table = new Rgba32[size];
        for (var i = 0; i < table.Length; i++)
            table[i] = new Rgba32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), 255);

        return table;
    }

    private static byte[] ToBytes(Rgba32[] pixels)
    {
        var result = new byte[checked(pixels.Length * 4)];
        for (var i = 0; i < pixels.Length; i++)
        {
            var pixel = pixels[i];
            var offset = i * 4;
            result[offset] = pixel.R;
            result[offset + 1] = pixel.G;
            result[offset + 2] = pixel.B;
            result[offset + 3] = pixel.A;
        }

        return result;
    }

    private static int ReadSubBlocks(BinaryReader reader, GifLzwScratch scratch)
    {
        var length = 0;
        while (true)
        {
            var blockSize = reader.ReadByte();
            if (blockSize == 0)
                return length;

            scratch.EnsureCompressedCapacity(checked(length + blockSize));
            var blockOffset = length;
            var remaining = (int) blockSize;
            while (remaining > 0)
            {
                var read = reader.Read(scratch.CompressedData, blockOffset, remaining);
                if (read == 0)
                    throw new InvalidDataException("Unexpected EOF in GIF sub-block.");

                blockOffset += read;
                remaining -= read;
            }

            length += blockSize;
        }
    }

    private static void SkipSubBlocks(BinaryReader reader)
    {
        var source = new GifSubBlockReader(reader);
        source.Drain();
    }

    private static GraphicControlExtension ReadGraphicControlExtension(BinaryReader reader)
    {
        var blockSize = reader.ReadByte();
        if (blockSize != 4)
        {
            for (var i = 0; i < blockSize; i++)
                _ = reader.ReadByte();

            if (reader.ReadByte() != 0)
                throw new InvalidDataException("Invalid GIF graphic control extension.");

            return GraphicControlExtension.Default;
        }

        var packed = reader.ReadByte();
        var delay = reader.ReadUInt16();
        var transparentIndex = reader.ReadByte();
        if (reader.ReadByte() != 0)
            throw new InvalidDataException("Invalid GIF graphic control extension terminator.");

        return new GraphicControlExtension(
            delay,
            (byte) ((packed >> 2) & 0x7),
            (packed & 0x1) != 0,
            transparentIndex);
    }

    private static bool TryReadByte(BinaryReader reader, out byte value)
    {
        if (reader.BaseStream.Position >= reader.BaseStream.Length)
        {
            value = default;
            return false;
        }

        value = reader.ReadByte();
        return true;
    }

    private sealed class GifLzwScratch
    {
        public readonly short[] Prefix = new short[GifMaxCodeSize];
        public readonly byte[] Suffix = new byte[GifMaxCodeSize];
        public readonly byte[] PixelStack = new byte[GifMaxCodeSize + 1];
        public byte[] ColorIndices = Array.Empty<byte>();
        public byte[] CompressedData = Array.Empty<byte>();

        public void EnsureColorCapacity(int pixelCount)
        {
            if (ColorIndices.Length < pixelCount)
                ColorIndices = new byte[pixelCount];
        }

        public void EnsureCompressedCapacity(int byteCount)
        {
            if (CompressedData.Length < byteCount)
            {
                var length = Math.Max(byteCount, Math.Max(256, CompressedData.Length * 2));
                var expanded = new byte[length];
                Array.Copy(CompressedData, expanded, CompressedData.Length);
                CompressedData = expanded;
            }
        }
    }

    private sealed class GifSubBlockReader
    {
        private readonly BinaryReader _reader;
        private int _remaining;
        private bool _finished;

        public GifSubBlockReader(BinaryReader reader)
        {
            _reader = reader;
        }

        public bool TryReadByte(out byte value)
        {
            if (_finished)
            {
                value = default;
                return false;
            }

            if (_remaining == 0)
            {
                var blockSize = _reader.ReadByte();
                if (blockSize == 0)
                {
                    _finished = true;
                    value = default;
                    return false;
                }

                _remaining = blockSize;
            }

            _remaining--;
            value = _reader.ReadByte();
            return true;
        }

        public void Drain()
        {
            while (TryReadByte(out _))
            {
            }
        }
    }

    private readonly record struct PreviousFrameState(
        int Left,
        int Top,
        int Width,
        int Height,
        byte DisposalMethod,
        Rgba32[]? RestoreSnapshot);

    private readonly record struct GraphicControlExtension(
        ushort DelayCentiseconds,
        byte DisposalMethod,
        bool TransparentColorFlag,
        byte TransparentColorIndex)
    {
        public static GraphicControlExtension Default => new(
            DelayCentiseconds: 0,
            DisposalMethod: 0,
            TransparentColorFlag: false,
            TransparentColorIndex: 0);
    }
}
