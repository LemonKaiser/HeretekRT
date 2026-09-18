using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Content.Client._WH40K.Visuals.WorldEffects;

/// <summary>
/// Minimal reader for the non-interlaced RGBA8 PNG files used by RSI states.
/// Content assemblies cannot use ImageSharp pixel-access delegates under the Robust sandbox.
/// </summary>
internal static class PngRgbaReader
{
    private const int MaxDimension = 4096;
    private const int MaxChunkLength = 32 * 1024 * 1024;
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool TryRead(Stream stream, out PngRgbaImage image)
    {
        image = default;

        try
        {
            using var reader = new BinaryReader(stream);
            if (!reader.ReadBytes(Signature.Length).SequenceEqual(Signature))
                return false;

            var width = 0;
            var height = 0;
            var hasHeader = false;
            var reachedEnd = false;
            var compressed = new List<byte>();

            while (stream.Position < stream.Length)
            {
                var length = ReadBigEndianInt32(reader);
                if (length < 0 || length > MaxChunkLength)
                    return false;

                var type = reader.ReadBytes(4);
                if (type.Length != 4)
                    return false;

                var data = reader.ReadBytes(length);
                if (data.Length != length || reader.ReadBytes(4).Length != 4)
                    return false;

                if (IsChunk(type, 'I', 'H', 'D', 'R'))
                {
                    if (hasHeader || data.Length != 13)
                        return false;

                    width = ReadBigEndianInt32(data, 0);
                    height = ReadBigEndianInt32(data, 4);
                    if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension ||
                        data[8] != 8 || data[9] != 6 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                        return false;

                    hasHeader = true;
                }
                else if (IsChunk(type, 'I', 'D', 'A', 'T'))
                {
                    if (!hasHeader || compressed.Count + data.Length > MaxChunkLength)
                        return false;

                    compressed.AddRange(data);
                }
                else if (IsChunk(type, 'I', 'E', 'N', 'D'))
                {
                    reachedEnd = true;
                    break;
                }
            }

            if (!hasHeader || !reachedEnd || compressed.Count < 6)
                return false;

            var zlib = compressed.ToArray();
            if ((zlib[0] & 0x0F) != 8 || ((zlib[0] << 8) + zlib[1]) % 31 != 0 || (zlib[1] & 0x20) != 0)
                return false;

            var stride = checked(width * 4);
            var packedStride = checked(stride + 1);
            var packed = new byte[checked(packedStride * height)];

            using (var compressedStream = new MemoryStream(zlib, 2, zlib.Length - 6, false))
            using (var inflater = new DeflateStream(compressedStream, CompressionMode.Decompress))
            {
                var offset = 0;
                while (offset < packed.Length)
                {
                    var read = inflater.Read(packed, offset, packed.Length - offset);
                    if (read == 0)
                        return false;

                    offset += read;
                }

                if (inflater.ReadByte() != -1)
                    return false;
            }

            var pixels = new byte[checked(stride * height)];
            for (var y = 0; y < height; y++)
            {
                var source = y * packedStride;
                var filter = packed[source++];
                if (filter > 4)
                    return false;

                var destination = y * stride;
                for (var x = 0; x < stride; x++)
                {
                    var left = x >= 4 ? pixels[destination + x - 4] : 0;
                    var above = y > 0 ? pixels[destination + x - stride] : 0;
                    var upperLeft = y > 0 && x >= 4 ? pixels[destination + x - stride - 4] : 0;
                    var predictor = filter switch
                    {
                        0 => 0,
                        1 => left,
                        2 => above,
                        3 => (left + above) / 2,
                        4 => Paeth(left, above, upperLeft),
                        _ => 0,
                    };

                    pixels[destination + x] = unchecked((byte) (packed[source + x] + predictor));
                }
            }

            image = new PngRgbaImage(width, height, pixels);
            return true;
        }
        catch (Exception)
        {
            image = default;
            return false;
        }
    }

    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        return bytes.Length == 4 ? ReadBigEndianInt32(bytes, 0) : -1;
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset)
    {
        return bytes[offset] << 24 |
               bytes[offset + 1] << 16 |
               bytes[offset + 2] << 8 |
               bytes[offset + 3];
    }

    private static bool IsChunk(byte[] type, char a, char b, char c, char d)
    {
        return type[0] == a && type[1] == b && type[2] == c && type[3] == d;
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        var prediction = left + above - upperLeft;
        var leftDistance = Math.Abs(prediction - left);
        var aboveDistance = Math.Abs(prediction - above);
        var upperLeftDistance = Math.Abs(prediction - upperLeft);

        if (leftDistance <= aboveDistance && leftDistance <= upperLeftDistance)
            return left;

        return aboveDistance <= upperLeftDistance ? above : upperLeft;
    }
}

internal readonly record struct PngRgbaImage(int Width, int Height, byte[] Pixels)
{
    public PngRgbaPixel GetPixel(int x, int y)
    {
        var index = (y * Width + x) * 4;
        return new PngRgbaPixel(Pixels[index], Pixels[index + 1], Pixels[index + 2], Pixels[index + 3]);
    }
}

internal readonly record struct PngRgbaPixel(byte R, byte G, byte B, byte A);
