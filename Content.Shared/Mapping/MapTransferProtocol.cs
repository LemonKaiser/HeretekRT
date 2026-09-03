using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Shared.Mapping;

public enum MapTransferDirection : byte
{
    Upload = 1,
    Download = 2,
}

public readonly record struct MapTransferHeader(
    MapTransferDirection Direction,
    Guid OperationId,
    byte[] Token,
    long DeclaredSize);

/// <summary>
///     Binary framing inside an <see cref="Robust.Shared.Network.Transfer.ITransferManager"/> stream.
///     The engine transfer key selects the feature; this header authenticates one concrete operation.
/// </summary>
public static class MapTransferProtocol
{
    public const string UploadTransferKey = "heretek-map-upload-v1";
    public const string DownloadTransferKey = "heretek-map-download-v1";

    public const int TokenLength = 32;
    private const uint Magic = 0x48524D50; // HRMP
    private const byte Version = 1;
    private const int HeaderLength = sizeof(uint) + sizeof(byte) + sizeof(byte) + 16 + TokenLength + sizeof(long);

    public static async ValueTask WriteHeaderAsync(
        Stream stream,
        MapTransferDirection direction,
        Guid operationId,
        byte[] token,
        long declaredSize,
        CancellationToken cancellationToken = default)
    {
        if (!IsDirectionValid(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));

        if (token.Length != TokenLength)
            throw new ArgumentException($"Token must be exactly {TokenLength} bytes.", nameof(token));

        if (declaredSize < 0)
            throw new ArgumentOutOfRangeException(nameof(declaredSize));

        var buffer = new byte[HeaderLength];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, Magic);
        buffer[sizeof(uint)] = Version;
        buffer[sizeof(uint) + sizeof(byte)] = (byte) direction;
        operationId.TryWriteBytes(buffer.AsSpan(sizeof(uint) + sizeof(byte) + sizeof(byte), 16));
        token.CopyTo(buffer, sizeof(uint) + sizeof(byte) + sizeof(byte) + 16);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(HeaderLength - sizeof(long)), declaredSize);

        await stream.WriteAsync(buffer, cancellationToken);
    }

    public static async ValueTask<MapTransferHeader> ReadHeaderAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[HeaderLength];
        await stream.ReadExactlyAsync(buffer, cancellationToken);

        if (BinaryPrimitives.ReadUInt32LittleEndian(buffer) != Magic || buffer[sizeof(uint)] != Version)
            throw new InvalidDataException("Invalid mapper map-transfer header.");

        var direction = (MapTransferDirection) buffer[sizeof(uint) + sizeof(byte)];
        if (!IsDirectionValid(direction))
            throw new InvalidDataException("Invalid mapper map-transfer direction.");

        var operationId = new Guid(buffer.AsSpan(sizeof(uint) + sizeof(byte) + sizeof(byte), 16));
        var token = buffer.AsSpan(sizeof(uint) + sizeof(byte) + sizeof(byte) + 16, TokenLength).ToArray();
        var declaredSize = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(HeaderLength - sizeof(long)));
        if (declaredSize < 0)
            throw new InvalidDataException("Invalid mapper map-transfer size.");

        return new MapTransferHeader(direction, operationId, token, declaredSize);
    }

    public static bool IsDirectionValid(MapTransferDirection direction)
    {
        return direction is MapTransferDirection.Upload or MapTransferDirection.Download;
    }
}
