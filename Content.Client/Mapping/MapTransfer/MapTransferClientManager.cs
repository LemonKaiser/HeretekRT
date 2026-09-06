using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.Mapping;
using Robust.Shared.Asynchronous;
using Robust.Shared.ContentPack;
using Robust.Shared.Network;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Utility;

namespace Content.Client.Mapping.MapTransfer;

/// <summary>
///     Owns the local file stream selected by the mapper. The server never receives a local path.
/// </summary>
public sealed partial class MapTransferClientManager
{
    private const int CopyBufferSize = 64 * 1024;
    private const int MaxLocalNameAttempts = 16;
    private const int PendingOperationTimeoutSeconds = 135;
    private static readonly ResPath DownloadDirectory = new("/SavedMaps");

    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private ITransferManager _transfers = default!;
    [Dependency] private ILogManager _logs = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private ITaskManager _tasks = default!;

    private readonly object _lock = new();
    private ISawmill _sawmill = default!;
    private PendingOperation? _pending;

    private sealed class PendingOperation(
        MapTransferDirection direction,
        Stream stream,
        long declaredSize,
        long maxBytes,
        ResPath? localDownloadPath = null,
        bool openDownloadFolderWhenCompleted = false)
    {
        public MapTransferDirection Direction { get; } = direction;
        public Stream Stream { get; } = stream;
        public long DeclaredSize { get; set; } = declaredSize;
        public long MaxBytes { get; } = maxBytes;
        public ResPath? LocalDownloadPath { get; } = localDownloadPath;
        public bool OpenDownloadFolderWhenCompleted { get; } = openDownloadFolderWhenCompleted;
        public Guid OperationId { get; set; }
        public byte[]? Token { get; set; }
        public CancellationTokenSource Cancellation { get; } = new();
        public bool Completed { get; set; }
    }

    public void Initialize()
    {
        _sawmill = _logs.GetSawmill("map_transfer");
        _net.RegisterNetMessage<MsgMapTransferGrant>(OnGrant);
        _net.RegisterNetMessage<MsgMapTransferReady>();
        _net.RegisterNetMessage<MsgMapTransferDownloadResult>();
        _net.Disconnect += OnDisconnect;
        _transfers.RegisterTransferMessage(
            MapTransferProtocol.DownloadTransferKey,
            OnDownloadTransferReceived,
            NetMessageAccept.Client);
    }

    public bool TryPrepareUpload(Stream stream, long maxBytes, out long declaredSize)
    {
        declaredSize = 0;
        if (!stream.CanRead || !stream.CanSeek)
        {
            stream.Dispose();
            return false;
        }

        try
        {
            stream.Position = 0;
            declaredSize = stream.Length;
        }
        catch (Exception e) when (e is IOException or NotSupportedException)
        {
            stream.Dispose();
            return false;
        }

        if (declaredSize <= 0 || declaredSize > maxBytes || !TrySetPending(new PendingOperation(MapTransferDirection.Upload, stream, declaredSize, maxBytes)))
        {
            stream.Dispose();
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Opens a collision-free .yml destination inside the client's writable data directory.
    ///     The map server receives neither this local path nor a client-selected filename.
    /// </summary>
    public bool TryPrepareDownload(string mapFileName, long maxBytes, bool openDownloadFolderWhenCompleted)
    {
        if (maxBytes <= 0 || !TryGetSafeMapFileName(mapFileName, out var safeName))
            return false;

        try
        {
            _resources.UserData.CreateDir(DownloadDirectory);
            for (var attempt = 0; attempt < MaxLocalNameAttempts; attempt++)
            {
                var name = attempt == 0
                    ? safeName
                    : $"{safeName[..^4]}-{attempt}.yml";
                var path = DownloadDirectory / name;
                Stream stream;
                try
                {
                    stream = _resources.UserData.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
                catch (IOException)
                {
                    continue;
                }

                if (TrySetPending(new PendingOperation(
                        MapTransferDirection.Download,
                        stream,
                        0,
                        maxBytes,
                        path,
                        openDownloadFolderWhenCompleted)))
                    return true;

                stream.Dispose();
                _resources.UserData.Delete(path);
                return false;
            }
        }
        catch (Exception e)
        {
            _sawmill.Warning("Could not create local mapper map download: {0}", e.GetType().Name);
        }

        return false;
    }

    public void OpenDownloadDirectory()
    {
        try
        {
            _resources.UserData.CreateDir(DownloadDirectory);
            _resources.UserData.OpenOsWindow(DownloadDirectory);
        }
        catch (Exception e)
        {
            _sawmill.Warning("Could not open local mapper map download directory: {0}", e.GetType().Name);
        }
    }

    public void CancelPending()
    {
        PendingOperation? pending;
        lock (_lock)
        {
            pending = _pending;
            _pending = null;
        }

        if (pending == null)
            return;

        pending.Cancellation.Cancel();
        pending.Stream.Dispose();
        DeleteIncompleteLocalDownload(pending);
        pending.Cancellation.Dispose();
    }

    private bool TrySetPending(PendingOperation pending)
    {
        lock (_lock)
        {
            if (_pending != null)
                return false;

            _pending = pending;
        }

        _ = ExpirePendingAsync(pending);
        return true;
    }

    private void OnGrant(MsgMapTransferGrant message)
    {
        PendingOperation? pending;
        lock (_lock)
        {
            pending = _pending;
            if (pending == null
                || pending.Direction != message.Direction
                || message.Token.Length != MapTransferProtocol.TokenLength
                || message.DeclaredSize <= 0
                || message.DeclaredSize > pending.MaxBytes
                || pending.Direction == MapTransferDirection.Upload && pending.DeclaredSize != message.DeclaredSize)
            {
                return;
            }

            pending.OperationId = message.OperationId;
            pending.Token = message.Token;
            pending.DeclaredSize = message.DeclaredSize;
        }

        if (message.Direction == MapTransferDirection.Upload)
        {
            _ = SendUploadAsync(pending);
            return;
        }

        _net.ClientSendMessage(new MsgMapTransferReady { OperationId = message.OperationId });
    }

    private async Task SendUploadAsync(PendingOperation operation)
    {
        try
        {
            if (_net.ServerChannel is not { } channel)
                throw new InvalidOperationException("Not connected to a server.");

            await using var transfer = _transfers.StartTransfer(channel, new TransferStartInfo
            {
                MessageKey = MapTransferProtocol.UploadTransferKey,
            });

            await MapTransferProtocol.WriteHeaderAsync(
                transfer,
                MapTransferDirection.Upload,
                operation.OperationId,
                operation.Token!,
                operation.DeclaredSize,
                operation.Cancellation.Token);

            await CopyExactlyAsync(
                operation.Stream,
                transfer,
                operation.DeclaredSize,
                operation.Cancellation.Token);
            await transfer.FlushAsync(operation.Cancellation.Token);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException)
        {
            _sawmill.Warning("Mapper map upload from local file failed: {0}", e.GetType().Name);
        }
        finally
        {
            ClearPending(operation);
        }
    }

    private void OnDownloadTransferReceived(TransferReceivedEvent received)
    {
        if (received.Channel != _net.ServerChannel)
        {
            _ = DrainAsync(received.DataStream, CancellationToken.None);
            return;
        }

        _ = ReceiveDownloadAsync(received.DataStream);
    }

    private async Task ReceiveDownloadAsync(Stream input)
    {
        PendingOperation? operation = null;
        try
        {
            await using (input)
            {
                using var headerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var header = await MapTransferProtocol.ReadHeaderAsync(input, headerTimeout.Token);

                lock (_lock)
                {
                    if (_pending is { Direction: MapTransferDirection.Download } pending
                        && pending.OperationId == header.OperationId
                        && pending.Token != null
                        && header.Direction == MapTransferDirection.Download
                        && header.DeclaredSize == pending.DeclaredSize
                        && TokensEqual(pending.Token, header.Token))
                    {
                        operation = pending;
                    }
                }

                if (operation == null)
                {
                    await DrainAsync(input, CancellationToken.None);
                    return;
                }

                var valid = await CopyReceivedMapAsync(input, operation);
                operation.Completed = valid;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _sawmill.Warning("Mapper map download to local file failed: {0}", e.GetType().Name);
        }
        finally
        {
            if (operation != null)
            {
                SendDownloadResult(operation);
                ClearPending(operation);
            }
        }
    }

    private static async Task<bool> CopyReceivedMapAsync(Stream input, PendingOperation operation)
    {
        var total = 0L;
        var valid = true;
        var buffer = new byte[CopyBufferSize];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), operation.Cancellation.Token);
            if (read == 0)
                break;

            if (read > operation.DeclaredSize - total)
            {
                valid = false;
                continue;
            }

            await operation.Stream.WriteAsync(buffer.AsMemory(0, read), operation.Cancellation.Token);
            total += read;
        }

        if (total != operation.DeclaredSize)
            valid = false;

        if (valid)
            await operation.Stream.FlushAsync(operation.Cancellation.Token);

        return valid;
    }

    private static async Task CopyExactlyAsync(Stream input, Stream output, long expectedBytes, CancellationToken cancellationToken)
    {
        var total = 0L;
        var buffer = new byte[CopyBufferSize];
        while (total < expectedBytes)
        {
            var requested = (int) Math.Min(buffer.Length, expectedBytes - total);
            var read = await input.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
            if (read == 0)
                throw new InvalidDataException("Selected local map ended before its declared size.");

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
        }

        if (await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
            throw new InvalidDataException("Selected local map changed while being uploaded.");
    }

    private static async Task DrainAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using (stream)
        {
            var buffer = new byte[CopyBufferSize];
            while (await stream.ReadAsync(buffer.AsMemory(), cancellationToken) != 0)
            {
            }
        }
    }

    private void ClearPending(PendingOperation operation)
    {
        lock (_lock)
        {
            if (_pending != operation)
                return;

            _pending = null;
        }

        operation.Stream.Dispose();
        if (!operation.Completed)
            DeleteIncompleteLocalDownload(operation);

        operation.Cancellation.Cancel();
        operation.Cancellation.Dispose();

        if (operation.Completed && operation.LocalDownloadPath != null && operation.OpenDownloadFolderWhenCompleted)
            _tasks.RunOnMainThread(OpenDownloadDirectory);
    }

    private async Task ExpirePendingAsync(PendingOperation operation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(PendingOperationTimeoutSeconds), operation.Cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_lock)
        {
            if (_pending != operation)
                return;

            _pending = null;
        }

        _sawmill.Warning("Mapper map transfer timed out locally before completion.");
        operation.Cancellation.Cancel();
        operation.Stream.Dispose();
        DeleteIncompleteLocalDownload(operation);
        operation.Cancellation.Dispose();
    }

    private void SendDownloadResult(PendingOperation operation)
    {
        try
        {
            _net.ClientSendMessage(new MsgMapTransferDownloadResult
            {
                OperationId = operation.OperationId,
                Completed = operation.Completed,
            });
        }
        catch (Exception e)
        {
            _sawmill.Warning("Could not acknowledge mapper map download: {0}", e.GetType().Name);
        }
    }

    private static bool TryGetSafeMapFileName(string mapFileName, out string safeName)
    {
        safeName = string.Empty;
        if (mapFileName.Length is 0 or > 128
            || !mapFileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
            || !ResPath.IsValidFilename(mapFileName))
        {
            return false;
        }

        safeName = mapFileName[..^4] + ".yml";
        return ResPath.IsValidFilename(safeName);
    }

    private void DeleteIncompleteLocalDownload(PendingOperation operation)
    {
        if (operation.LocalDownloadPath is not { } path)
            return;

        try
        {
            if (_resources.UserData.Exists(path))
                _resources.UserData.Delete(path);
        }
        catch (Exception e)
        {
            _sawmill.Warning("Could not remove incomplete local mapper map download: {0}", e.GetType().Name);
        }
    }

    private static bool TokensEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args)
    {
        CancelPending();
    }
}
