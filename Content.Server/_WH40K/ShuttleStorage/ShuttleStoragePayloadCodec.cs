using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Content.Shared._WH40K.ShuttleStorage;

namespace Content.Server._WH40K.ShuttleStorage;

public static class ShuttleStoragePayloadCodec
{
    private static readonly byte[] Magic = "SHB1"u8.ToArray();
    private const int HeaderLength = 8;

    public static byte[] Pack(byte[] yaml)
    {
        CheckSize(yaml.Length);
        var packed = new byte[HeaderLength + BrotliEncoder.GetMaxCompressedLength(yaml.Length)];
        Magic.AsSpan().CopyTo(packed);
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(Magic.Length), yaml.Length);
        if (!BrotliEncoder.TryCompress(yaml, packed.AsSpan(HeaderLength), out var written, quality: 4, window: 20))
            throw new InvalidDataException("Cannot compress the shuttle snapshot.");

        var length = HeaderLength + written;
        // Small or incompressible documents retain the supported legacy representation.
        if (length >= yaml.Length && !yaml.AsSpan().StartsWith(Magic))
            return yaml;
        CheckSize(length);
        return packed.AsSpan(0, length).ToArray();
    }

    public static byte[] Unpack(byte[] payload)
    {
        CheckSize(payload.Length);
        if (!payload.AsSpan().StartsWith(Magic))
            return payload;
        if (payload.Length <= HeaderLength)
            throw new InvalidDataException("The compressed shuttle snapshot header is incomplete.");

        var length = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(Magic.Length));
        CheckSize(length);
        var yaml = new byte[length];
        using var decoder = new BrotliDecoder();
        var status = decoder.Decompress(payload.AsSpan(HeaderLength), yaml, out var consumed, out var written);
        if (status != OperationStatus.Done || consumed != payload.Length - HeaderLength || written != length)
            throw new InvalidDataException("The compressed shuttle snapshot is truncated, invalid or has an incorrect size.");
        return yaml;
    }

    private static void CheckSize(int length)
    {
        if (length is <= 0 or > ShuttleStorageLimits.Bytes)
            throw new InvalidDataException("The shuttle snapshot exceeds its size limits.");
    }
}
