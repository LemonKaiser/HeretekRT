using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.Administration.Logs;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Mapping;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Mapping;

/// <summary>
///     Server authority for the mapper map-transfer panel and transfer streams.
/// </summary>
public sealed class MapTransferManager
{
    private const int CopyBufferSize = 64 * 1024;
    private const int MaxFileMiB = 16;
    private const int MaxTotalMiB = 512;
    private const int MaxConcurrentUploads = 2;
    private const int MaxConcurrentDownloads = 4;
    private const int MaxEntriesPerDirectory = 100;
    private const int OperationTimeoutSeconds = 120;
    private const int MaxYamlDepth = 128;
    private const int MaxYamlNodes = 1_000_000;
    private const int MaxQuotaScanEntries = 10_000;
    private const int MaxDirectoryEntriesToScan = 1_000;
    private const int MaxFolderDepth = 16;
    private const int CancelledUploadGraceSeconds = 30;
    internal const int MaxMapBaseNameLength = 124;
    private const int MaxGeneratedMapNumber = 1_000_000;
    private static readonly char[] InvalidMapFileNameCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private EuiManager _euis = default!;
    [Dependency] private ILogManager _logs = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private IServerNetManager _net = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ITransferManager _transfers = default!;

    private readonly Dictionary<Guid, PendingOperation> _operations = [];
    private readonly Dictionary<Guid, CancelledUpload> _cancelledUploads = [];
    private readonly HashSet<INetChannel> _uploadHeaderChannels = [];
    private ISawmill _sawmill = default!;
    private int _activeUploadWorkers;

    private enum UploadStartResult : byte
    {
        Accepted,
        Cancelled,
        Rejected,
        Busy,
    }

    private sealed class CancelledUpload(
        INetChannel channel,
        NetUserId userId,
        byte[] token,
        long declaredSize,
        TimeSpan expiresAt)
    {
        public INetChannel Channel { get; } = channel;
        public NetUserId UserId { get; } = userId;
        public byte[] Token { get; } = token;
        public long DeclaredSize { get; } = declaredSize;
        public TimeSpan ExpiresAt { get; } = expiresAt;
    }

    private sealed class PendingOperation(
        Guid id,
        ICommonSession session,
        MapTransferEui ui,
        MapTransferDirection direction,
        byte[] token,
        long declaredSize,
        ResPath? sourcePath,
        ResPath? temporaryPath,
        ResPath? finalPath,
        long maxFileBytes,
        int maxYamlDepth,
        int maxYamlNodes)
    {
        public Guid Id { get; } = id;
        public ICommonSession Session { get; } = session;
        public MapTransferEui Ui { get; } = ui;
        public MapTransferDirection Direction { get; } = direction;
        public byte[] Token { get; } = token;
        public long DeclaredSize { get; } = declaredSize;
        public ResPath? SourcePath { get; } = sourcePath;
        public ResPath? TemporaryPath { get; } = temporaryPath;
        public ResPath? FinalPath { get; } = finalPath;
        public long MaxFileBytes { get; } = maxFileBytes;
        public int MaxYamlDepth { get; } = maxYamlDepth;
        public int MaxYamlNodes { get; } = maxYamlNodes;
        public CancellationTokenSource Cancellation { get; } = new();
        public bool Started { get; set; }
        public bool Sent { get; set; }
        public bool? ClientCompleted { get; set; }
        public long SentBytes { get; set; }
        public int CancellationDisposed;
    }

    public bool UploadsAllowed => _cfg.GetCVar(CCVars.MapTransferUploadEnabled);
    public bool DownloadsAllowed => _cfg.GetCVar(CCVars.MapTransferDownloadEnabled);
    public bool IsFeatureEnabled => UploadsAllowed || DownloadsAllowed;
    public long MaxFileBytes => MaxFileMiB * 1024L * 1024L;

    public void Initialize()
    {
        _sawmill = _logs.GetSawmill("map_transfer");

        _net.RegisterNetMessage<MsgMapTransferGrant>();
        _net.RegisterNetMessage<MsgMapTransferReady>(OnDownloadReady);
        _net.RegisterNetMessage<MsgMapTransferDownloadResult>(OnDownloadResult);
        _net.Disconnect += OnDisconnect;
        _transfers.RegisterTransferMessage(
            MapTransferProtocol.UploadTransferKey,
            OnUploadTransferReceived,
            NetMessageAccept.Server);

        if (TryGetRootDirectory(out var root))
            CleanupIncompleteUploads(root);
    }

    public bool CanUse(ICommonSession session)
    {
        return IsFeatureEnabled && HasMapperAccess(session);
    }

    private bool CanUpload(ICommonSession session)
    {
        return UploadsAllowed && HasMapperAccess(session);
    }

    private bool CanDownload(ICommonSession session)
    {
        return DownloadsAllowed && HasMapperAccess(session);
    }

    private bool HasMapperAccess(ICommonSession session)
    {
        return session.Status != SessionStatus.Disconnected
            && session.Channel.IsConnected
            && _admins.IsAdmin(session)
            && _admins.HasAdminFlag(session, AdminFlags.Mapping);
    }

    public bool TryOpenEui(ICommonSession session)
    {
        if (!CanUse(session) || !TryGetRootDirectory(out _))
            return false;

        _euis.OpenEui(new MapTransferEui(), session);
        return true;
    }

    public bool TryGetRootDirectory(out ResPath root)
    {
        if (!TryGetRootDirectoryUnchecked(out root))
            return false;

        return TryCreateTransferDirectories(root)
            && TryEnsureNoReparsePoints(root, allowMissingFinal: false)
            && TryEnsureNoReparsePoints(root / ".incoming", allowMissingFinal: false);
    }

    public IEnumerable<ResPath> ListFolders(ResPath directory)
    {
        if (!TryGetRootDirectory(out var root)
            || !IsUsableDirectory(directory, root)
            || GetDirectoryDepth(directory, root) >= MaxFolderDepth)
            return [];

        var folders = new List<ResPath>();
        var entries = 0;
        foreach (var name in _resources.UserData.DirectoryEntries(directory).OrderBy(x => x, StringComparer.Ordinal))
        {
            if (++entries > MaxDirectoryEntriesToScan)
                break;

            if (folders.Count >= MaxEntriesPerDirectory)
                break;

            if (!IsSafeEntryName(name) || string.Equals(name, ".incoming", StringComparison.Ordinal))
                continue;

            var child = directory / name;
            if (_resources.UserData.IsDir(child) && TryEnsureNoReparsePoints(child, allowMissingFinal: false))
                folders.Add(child);
        }

        return folders;
    }

    public IEnumerable<ResPath> ListValidatedMaps(ResPath directory)
    {
        if (!TryGetRootDirectory(out var root) || !IsUsableDirectory(directory, root))
            return [];

        var maps = new List<ResPath>();
        var entries = 0;
        foreach (var name in _resources.UserData.DirectoryEntries(directory).OrderBy(x => x, StringComparer.Ordinal))
        {
            if (++entries > MaxDirectoryEntriesToScan)
                break;

            if (maps.Count >= MaxEntriesPerDirectory)
                break;

            if (!IsSafeEntryName(name) || !string.Equals(Path.GetExtension(name), ".yml", StringComparison.OrdinalIgnoreCase))
                continue;

            var candidate = directory / name;
            if (!TryGetMapSize(candidate, out _))
                continue;

            if (MapTransferYamlValidator.TryValidate(
                    _resources.UserData,
                    candidate,
                    MaxFileBytes,
                    MaxYamlDepth,
                    MaxYamlNodes))
            {
                maps.Add(candidate);
            }
        }

        return maps;
    }

    public bool TryGetMapSize(ResPath path, out long size)
    {
        size = 0;
        if (!TryGetRootDirectory(out var root)
            || !IsPathInside(path, root)
            || !string.Equals(path.Extension, "yml", StringComparison.OrdinalIgnoreCase)
            || !TryEnsureNoReparsePoints(path, allowMissingFinal: false))
        {
            return false;
        }

        try
        {
            using var stream = _resources.UserData.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            size = stream.Length;
            return size > 0 && size <= MaxFileBytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public MapTransferUiStatus TryCreateUpload(
        ICommonSession session,
        ResPath directory,
        long declaredSize,
        MapTransferEui ui)
    {
        if (!HasMapperAccess(session))
            return MapTransferUiStatus.AccessDenied;

        if (!UploadsAllowed)
            return MapTransferUiStatus.Disabled;

        if (!TryGetRootDirectory(out var root) || !IsUsableDirectory(directory, root))
            return MapTransferUiStatus.Failed;

        var maxFileBytes = MaxFileBytes;
        if (declaredSize <= 0 || maxFileBytes <= 0 || declaredSize > maxFileBytes)
            return MapTransferUiStatus.FileTooLarge;

        if (_operations.Values.Any(operation => operation.Session.UserId == session.UserId))
            return MapTransferUiStatus.Failed;

        if (_operations.Values.Count(operation => operation.Direction == MapTransferDirection.Upload) >= MaxConcurrentUploads)
            return MapTransferUiStatus.Busy;

        var maxTotalBytes = MaxTotalMiB * 1024L * 1024L;
        if (!HasTotalQuota(root, declaredSize, maxTotalBytes))
            return MapTransferUiStatus.QuotaExceeded;

        var id = Guid.NewGuid();
        var token = RandomNumberGenerator.GetBytes(MapTransferProtocol.TokenLength);
        var incoming = root / ".incoming";
        var temporary = incoming / $"{id:N}.part";
        var final = FindFreeUploadedMapPath(directory);
        if (final == null)
            return MapTransferUiStatus.Failed;

        var operation = new PendingOperation(
            id,
            session,
            ui,
            MapTransferDirection.Upload,
            token,
            declaredSize,
            null,
            temporary,
            final.Value,
            maxFileBytes,
            MaxYamlDepth,
            MaxYamlNodes);

        _operations.Add(id, operation);
        SendGrant(operation);
        ScheduleTimeout(operation);
        return MapTransferUiStatus.Uploading;
    }

    /// <summary>
    ///     Renames a visible map without trusting a path or a file extension from the client.
    /// </summary>
    public MapTransferUiStatus TryRenameMap(ICommonSession session, ResPath mapPath, string requestedName)
    {
        if (!HasMapperAccess(session))
            return MapTransferUiStatus.AccessDenied;

        if (!TryGetRootDirectory(out var root)
            || !IsUsableDirectory(mapPath.Directory, root)
            || !TryGetMapSize(mapPath, out _)
            || !MapTransferYamlValidator.TryValidate(
                _resources.UserData,
                mapPath,
                MaxFileBytes,
                MaxYamlDepth,
                MaxYamlNodes))
        {
            return MapTransferUiStatus.InvalidMap;
        }

        if (!TryNormalizeMapBaseName(requestedName, out var name))
            return MapTransferUiStatus.InvalidName;

        if (_operations.Values.Any(operation => operation.Session.UserId == session.UserId))
            return MapTransferUiStatus.Busy;

        var destination = mapPath.Directory / $"{name}.yml";
        if (destination == mapPath)
            return MapTransferUiStatus.Completed;

        if (!TryEnsureNoReparsePoints(mapPath, allowMissingFinal: false)
            || !TryEnsureNoReparsePoints(destination, allowMissingFinal: true))
        {
            return MapTransferUiStatus.Failed;
        }

        if (!TryHasMapDestinationConflict(mapPath, destination, out var hasConflict))
            return MapTransferUiStatus.Failed;

        if (hasConflict)
            return MapTransferUiStatus.NameTaken;

        try
        {
            // WritableDirProvider uses File.Move, which refuses to overwrite an existing destination.
            // The existence check above gives a clear UI error; File.Move is the final race-safe guard.
            _resources.UserData.Rename(mapPath, destination);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return _resources.UserData.Exists(destination)
                ? MapTransferUiStatus.NameTaken
                : MapTransferUiStatus.Failed;
        }

        _adminLog.Add(
            LogType.Action,
            LogImpact.Medium,
            $"{session:actor} renamed mapper map {mapPath.Filename} to {destination.Filename}.");
        _sawmill.Info("Mapper map rename by {0}: {1} -> {2}.", session.UserId, mapPath.Filename, destination.Filename);
        return MapTransferUiStatus.Completed;
    }

    public MapTransferUiStatus TryCreateDownload(ICommonSession session, ResPath mapPath, MapTransferEui ui)
    {
        if (!HasMapperAccess(session))
            return MapTransferUiStatus.AccessDenied;

        if (!DownloadsAllowed)
            return MapTransferUiStatus.Disabled;

        if (_operations.Values.Any(operation => operation.Session.UserId == session.UserId))
            return MapTransferUiStatus.Failed;

        if (_operations.Values.Count(operation => operation.Direction == MapTransferDirection.Download) >= MaxConcurrentDownloads)
            return MapTransferUiStatus.Busy;

        if (!TryGetMapSize(mapPath, out var size)
            || !MapTransferYamlValidator.TryValidate(
                _resources.UserData,
                mapPath,
                MaxFileBytes,
                MaxYamlDepth,
                MaxYamlNodes))
        {
            return MapTransferUiStatus.InvalidMap;
        }

        var id = Guid.NewGuid();
        var operation = new PendingOperation(
            id,
            session,
            ui,
            MapTransferDirection.Download,
            RandomNumberGenerator.GetBytes(MapTransferProtocol.TokenLength),
            size,
            mapPath,
            null,
            null,
            MaxFileBytes,
            MaxYamlDepth,
            MaxYamlNodes);

        _operations.Add(id, operation);
        SendGrant(operation);
        ScheduleTimeout(operation);
        return MapTransferUiStatus.Downloading;
    }

    public void CancelForUi(MapTransferEui ui, MapTransferUiStatus status)
    {
        foreach (var operation in _operations.Values.Where(operation => operation.Ui == ui).ToArray())
        {
            CancelOperation(operation, status);
        }
    }

    private void SendGrant(PendingOperation operation)
    {
        _net.ServerSendMessage(new MsgMapTransferGrant
        {
            OperationId = operation.Id,
            Direction = operation.Direction,
            Token = operation.Token.ToArray(),
            DeclaredSize = operation.DeclaredSize,
        }, operation.Session.Channel);
    }

    private void ScheduleTimeout(PendingOperation operation)
    {
        Robust.Shared.Timing.Timer.Spawn(TimeSpan.FromSeconds(OperationTimeoutSeconds), () =>
            _tasks.RunOnMainThread(() =>
            {
                if (_operations.TryGetValue(operation.Id, out var tracked) && tracked == operation)
                    CancelOperation(operation, MapTransferUiStatus.TimedOut);
            }));
    }

    private void OnUploadTransferReceived(TransferReceivedEvent received)
    {
        if (!UploadsAllowed || !HasExpectedUploadForChannel(received.Channel))
        {
            received.DataStream.Dispose();
            received.Channel.Disconnect("Mapper map-transfer is disabled.");
            return;
        }

        // A single pending operation may own only one transfer header. This rejects duplicate streams before
        // allocating a parser task and keeps unauthorised clients from consuming the upload worker budget.
        if (!_uploadHeaderChannels.Add(received.Channel))
        {
            received.DataStream.Dispose();
            received.Channel.Disconnect("Duplicate mapper map-transfer upload.");
            return;
        }

        _ = ReceiveUploadAsync(received);
    }

    private async Task ReceiveUploadAsync(TransferReceivedEvent received)
    {
        PendingOperation? operation = null;
        var ownsBodyWorker = false;
        try
        {
            using var stream = received.DataStream;
            using var headerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var header = await MapTransferProtocol.ReadHeaderAsync(stream, headerTimeout.Token);

            var startResult = UploadStartResult.Rejected;
            await RunOnMainThread(() =>
            {
                startResult = TryStartUpload(header, received.Channel, out operation);
            });

            if (startResult == UploadStartResult.Cancelled)
            {
                await DrainCancelledUploadAsync(stream);
                return;
            }

            if (startResult == UploadStartResult.Busy)
            {
                if (operation != null)
                    await RunOnMainThread(() => CancelOperation(operation, MapTransferUiStatus.Busy));

                return;
            }

            if (startResult != UploadStartResult.Accepted || operation == null)
            {
                await RunOnMainThread(() => CancelPendingUploadForChannel(received.Channel, MapTransferUiStatus.Failed));
                return;
            }

            ownsBodyWorker = true;
            var result = await ReceiveUploadFileAsync(operation, stream);
            await RunOnMainThread(() => CompleteUpload(operation, result.status, result.hash, result.bytes));
        }
        catch (OperationCanceledException)
        {
            if (operation != null)
                await RunOnMainThread(() => CancelOperation(operation, MapTransferUiStatus.TimedOut));
            else
                await RunOnMainThread(() => CancelPendingUploadForChannel(received.Channel, MapTransferUiStatus.TimedOut));
        }
        catch (Exception e)
        {
            _sawmill.Warning("Mapper map upload failed: {0}", e.GetType().Name);
            if (operation != null)
                await RunOnMainThread(() => CompleteUpload(operation, MapTransferUiStatus.Failed, null, 0));
            else
                await RunOnMainThread(() => CancelPendingUploadForChannel(received.Channel, MapTransferUiStatus.Failed));
        }
        finally
        {
            if (ownsBodyWorker)
                Interlocked.Decrement(ref _activeUploadWorkers);

            _tasks.RunOnMainThread(() => _uploadHeaderChannels.Remove(received.Channel));
            if (operation != null)
                DisposeCancellation(operation);
        }
    }

    private UploadStartResult TryStartUpload(MapTransferHeader header, INetChannel channel, out PendingOperation? operation)
    {
        operation = null;
        PruneCancelledUploads();
        if (header.Direction != MapTransferDirection.Upload)
        {
            return UploadStartResult.Rejected;
        }

        if (_operations.TryGetValue(header.OperationId, out var pending))
        {
            if (pending.Direction != MapTransferDirection.Upload
                || pending.Started
                || pending.Session.UserId != channel.UserId
                || pending.Session.Channel != channel
                || pending.DeclaredSize != header.DeclaredSize
                || !CryptographicOperations.FixedTimeEquals(pending.Token, header.Token)
                || !CanUpload(pending.Session))
            {
                return UploadStartResult.Rejected;
            }

            pending.Started = true;
            operation = pending;
            if (Interlocked.Increment(ref _activeUploadWorkers) > MaxConcurrentUploads)
            {
                Interlocked.Decrement(ref _activeUploadWorkers);
                return UploadStartResult.Busy;
            }

            return UploadStartResult.Accepted;
        }

        if (_cancelledUploads.Remove(header.OperationId, out var cancelled)
            && cancelled.Channel == channel
            && cancelled.UserId == channel.UserId
            && cancelled.DeclaredSize == header.DeclaredSize
            && CryptographicOperations.FixedTimeEquals(cancelled.Token, header.Token))
        {
            return UploadStartResult.Cancelled;
        }

        return UploadStartResult.Rejected;
    }

    private async Task<(MapTransferUiStatus status, string? hash, long bytes)> ReceiveUploadFileAsync(
        PendingOperation operation,
        Stream input)
    {
        if (operation.TemporaryPath is not { } temporary
            || !TryEnsureNoReparsePoints(temporary, allowMissingFinal: true))
        {
            return (MapTransferUiStatus.Failed, null, 0);
        }

        var bytes = 0L;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[CopyBufferSize];

            // The validator reopens this file through IWritableDirProvider. Close the exclusive writer first:
            // attempting to validate while FileShare.None is still held makes every otherwise valid upload look invalid.
            await using (var output = _resources.UserData.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(), operation.Cancellation.Token).ConfigureAwait(false);
                    if (read == 0)
                        break;

                    if (read > operation.DeclaredSize - bytes || read > operation.MaxFileBytes - bytes)
                        return (MapTransferUiStatus.FileTooLarge, null, bytes);

                    await output.WriteAsync(buffer.AsMemory(0, read), operation.Cancellation.Token).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    bytes += read;
                }

                if (bytes != operation.DeclaredSize)
                    return (MapTransferUiStatus.Failed, null, bytes);

                await output.FlushAsync(operation.Cancellation.Token).ConfigureAwait(false);
            }

            if (!MapTransferYamlValidator.TryValidate(
                    _resources.UserData,
                    temporary,
                    operation.MaxFileBytes,
                    operation.MaxYamlDepth,
                    operation.MaxYamlNodes))
            {
                return (MapTransferUiStatus.InvalidMap, null, bytes);
            }

            return (MapTransferUiStatus.Completed, Convert.ToHexString(hash.GetHashAndReset()), bytes);
        }
        catch (OperationCanceledException)
        {
            return (MapTransferUiStatus.TimedOut, null, bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (MapTransferUiStatus.Failed, null, bytes);
        }
    }

    private void CompleteUpload(PendingOperation operation, MapTransferUiStatus status, string? hash, long bytes)
    {
        if (!_operations.TryGetValue(operation.Id, out var tracked) || tracked != operation)
        {
            DeleteTemporaryFile(operation);
            return;
        }

        if (status == MapTransferUiStatus.Completed
            && CanUpload(operation.Session)
            && operation.TemporaryPath is { } temporary
            && operation.FinalPath is { } final)
        {
            try
            {
                if (!TryEnsureNoReparsePoints(temporary, allowMissingFinal: false)
                    || !TryEnsureNoReparsePoints(final, allowMissingFinal: true))
                {
                    status = MapTransferUiStatus.Failed;
                }
                else
                {
                    _resources.UserData.Rename(temporary, final);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                status = MapTransferUiStatus.Failed;
            }
        }
        else if (status == MapTransferUiStatus.Completed)
        {
            status = MapTransferUiStatus.AccessDenied;
        }

        if (status != MapTransferUiStatus.Completed)
            DeleteTemporaryFile(operation);

        FinishOperation(operation, status, hash, bytes, refreshDirectory: status == MapTransferUiStatus.Completed);
    }

    private void OnDownloadReady(MsgMapTransferReady message)
    {
        if (!_operations.TryGetValue(message.OperationId, out var operation)
            || operation.Direction != MapTransferDirection.Download
            || operation.Started
            || operation.Session.UserId != message.MsgChannel.UserId
            || operation.Session.Channel != message.MsgChannel
            || !CanDownload(operation.Session))
        {
            return;
        }

        try
        {
            operation.Started = true;
            var stream = _transfers.StartTransfer(message.MsgChannel, new TransferStartInfo
            {
                MessageKey = MapTransferProtocol.DownloadTransferKey,
            });
            _ = SendDownloadAsync(operation, stream);
        }
        catch (Exception e)
        {
            _sawmill.Warning("Could not start mapper map download: {0}", e.GetType().Name);
            FinishOperation(operation, MapTransferUiStatus.Failed, null, 0, refreshDirectory: false);
        }
    }

    private void OnDownloadResult(MsgMapTransferDownloadResult message)
    {
        if (!_operations.TryGetValue(message.OperationId, out var operation)
            || operation.Direction != MapTransferDirection.Download
            || !operation.Started
            || operation.Session.UserId != message.MsgChannel.UserId
            || operation.Session.Channel != message.MsgChannel)
        {
            return;
        }

        operation.ClientCompleted = message.Completed;
        FinishAcknowledgedDownload(operation);
    }

    private async Task SendDownloadAsync(PendingOperation operation, Stream output)
    {
        MapTransferUiStatus status = MapTransferUiStatus.Failed;
        long bytes = 0;
        try
        {
            await using (output)
            {
                if (operation.SourcePath is not { } source
                    || !TryGetMapSize(source, out var size)
                    || size != operation.DeclaredSize
                    || !MapTransferYamlValidator.TryValidate(
                        _resources.UserData,
                        source,
                        operation.MaxFileBytes,
                        operation.MaxYamlDepth,
                        operation.MaxYamlNodes))
                {
                    throw new InvalidDataException("Map is no longer valid for download.");
                }

                await MapTransferProtocol.WriteHeaderAsync(
                    output,
                    MapTransferDirection.Download,
                    operation.Id,
                    operation.Token,
                    size,
                    operation.Cancellation.Token);

                await using var input = _resources.UserData.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                var buffer = new byte[CopyBufferSize];
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(), operation.Cancellation.Token).ConfigureAwait(false);
                    if (read == 0)
                        break;

                    await output.WriteAsync(buffer.AsMemory(0, read), operation.Cancellation.Token).ConfigureAwait(false);
                    bytes += read;
                }

                if (bytes != size)
                    throw new InvalidDataException("Map changed while being sent.");

                await output.FlushAsync(operation.Cancellation.Token).ConfigureAwait(false);
                status = MapTransferUiStatus.Completed;
            }
        }
        catch (OperationCanceledException)
        {
            status = MapTransferUiStatus.TimedOut;
        }
        catch (Exception e)
        {
            _sawmill.Warning("Mapper map download failed: {0}", e.GetType().Name);
        }

        await RunOnMainThread(() =>
        {
            if (status != MapTransferUiStatus.Completed)
            {
                FinishOperation(operation, status, null, bytes, refreshDirectory: false);
                return;
            }

            if (!_operations.TryGetValue(operation.Id, out var tracked) || tracked != operation)
            {
                DisposeCancellation(operation);
                return;
            }

            // The receiver may acknowledge immediately after the transfer stream finishes, before this
            // continuation reaches the main thread. Preserve that result and finish only after both sides agree.
            operation.Sent = true;
            operation.SentBytes = bytes;
            FinishAcknowledgedDownload(operation);
        });

        if (status != MapTransferUiStatus.Completed)
            DisposeCancellation(operation);
    }

    private void FinishAcknowledgedDownload(PendingOperation operation)
    {
        if (!operation.Sent || operation.ClientCompleted is not { } completed)
            return;

        FinishOperation(
            operation,
            completed ? MapTransferUiStatus.Completed : MapTransferUiStatus.Failed,
            null,
            operation.SentBytes,
            refreshDirectory: false);
    }

    private void FinishOperation(
        PendingOperation operation,
        MapTransferUiStatus status,
        string? hash,
        long bytes,
        bool refreshDirectory)
    {
        if (!_operations.Remove(operation.Id))
            return;

        operation.Cancellation.Cancel();
        if (status is not MapTransferUiStatus.Completed)
            DeleteTemporaryFile(operation);

        var impact = status == MapTransferUiStatus.Completed ? LogImpact.Medium : LogImpact.Low;
        var direction = operation.Direction == MapTransferDirection.Upload ? "uploaded" : "downloaded";
        var hashSuffix = hash == null ? string.Empty : $" SHA-256 {hash}.";
        _adminLog.Add(
            LogType.Action,
            impact,
            $"{operation.Session:actor} {direction} mapper map operation {operation.Id} ({bytes} bytes), result {status}.{hashSuffix}");
        _sawmill.Info(
            "Mapper map transfer {0} by {1}: {2}, {3} bytes.",
            operation.Direction,
            operation.Session.UserId,
            status,
            bytes);

        operation.Ui.SetOperationStatus(status, refreshDirectory);
        DisposeCancellation(operation);
    }

    private void CancelOperation(PendingOperation operation, MapTransferUiStatus status)
    {
        if (!_operations.Remove(operation.Id))
            return;

        if (operation.Direction == MapTransferDirection.Upload && !operation.Started)
            RememberCancelledUpload(operation);

        operation.Cancellation.Cancel();
        if (!operation.Started || (operation.Direction == MapTransferDirection.Download && operation.Sent))
        {
            DeleteTemporaryFile(operation);
            DisposeCancellation(operation);
        }

        _adminLog.Add(
            LogType.Action,
            LogImpact.Low,
            $"{operation.Session:actor} mapper map transfer operation {operation.Id} was cancelled with result {status}.");
        operation.Ui.SetOperationStatus(status, refreshDirectory: false);
    }

    private bool HasExpectedUploadForChannel(INetChannel channel)
    {
        PruneCancelledUploads();
        return _operations.Values.Any(operation =>
                   operation.Direction == MapTransferDirection.Upload
                   && !operation.Started
                   && operation.Session.Channel == channel
                   && CanUpload(operation.Session))
               || _cancelledUploads.Values.Any(operation =>
                   operation.Channel == channel
                   && operation.UserId == channel.UserId);
    }

    private void CancelPendingUploadForChannel(INetChannel channel, MapTransferUiStatus status)
    {
        foreach (var operation in _operations.Values
                     .Where(operation => operation.Direction == MapTransferDirection.Upload
                                         && !operation.Started
                                         && operation.Session.Channel == channel)
                     .ToArray())
        {
            CancelOperation(operation, status);
        }
    }

    private void RememberCancelledUpload(PendingOperation operation)
    {
        PruneCancelledUploads();
        _cancelledUploads[operation.Id] = new CancelledUpload(
            operation.Session.Channel,
            operation.Session.UserId,
            operation.Token.ToArray(),
            operation.DeclaredSize,
            _timing.RealTime + TimeSpan.FromSeconds(CancelledUploadGraceSeconds));
    }

    private void PruneCancelledUploads()
    {
        foreach (var id in _cancelledUploads
                     .Where(pair => pair.Value.ExpiresAt <= _timing.RealTime)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _cancelledUploads.Remove(id);
        }
    }

    private static async Task DrainCancelledUploadAsync(Stream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[CopyBufferSize];
        while (await stream.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false) != 0)
        {
        }
    }

    private static void DisposeCancellation(PendingOperation operation)
    {
        if (Interlocked.Exchange(ref operation.CancellationDisposed, 1) == 0)
            operation.Cancellation.Dispose();
    }

    private void DeleteTemporaryFile(PendingOperation operation)
    {
        if (operation.TemporaryPath is not { } temporary)
            return;

        try
        {
            if (_resources.UserData.Exists(temporary))
                _resources.UserData.Delete(temporary);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _sawmill.Warning("Could not remove incomplete mapper map transfer {0}.", operation.Id);
        }
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args)
    {
        foreach (var operation in _operations.Values
                     .Where(operation => operation.Session.UserId == args.Channel.UserId)
                     .ToArray())
        {
            CancelOperation(operation, MapTransferUiStatus.Failed);
        }
    }

    private bool HasTotalQuota(ResPath root, long newReservation, long maxTotalBytes)
    {
        var total = 0L;
        try
        {
            if (!TryEnumerateMapFiles(root, out var files))
                return false;

            foreach (var path in files)
            {
                if (!TryGetStoredFileSize(path, root, out var size))
                    return false;

                total = checked(total + size);
                if (total > maxTotalBytes)
                    return false;
            }

            var reserved = _operations.Values
                .Where(operation => operation.Direction == MapTransferDirection.Upload)
                .Sum(operation => operation.DeclaredSize);
            return total <= maxTotalBytes - reserved - newReservation;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private bool TryEnumerateMapFiles(ResPath root, out List<ResPath> files)
    {
        files = [];
        var entries = 0;
        return TryEnumerateMapFiles(root, root, files, ref entries, 0);
    }

    private bool TryEnumerateMapFiles(
        ResPath root,
        ResPath directory,
        List<ResPath> files,
        ref int entries,
        int depth)
    {
        if (depth > MaxFolderDepth || !IsUsableDirectory(directory, root))
            return false;

        try
        {
            foreach (var name in _resources.UserData.DirectoryEntries(directory))
            {
                if (++entries > MaxQuotaScanEntries)
                    return false;

                if (directory == root && string.Equals(name, ".incoming", StringComparison.Ordinal))
                    continue;

                if (!IsSafeEntryName(name))
                    return false;

                var path = directory / name;
                if (_resources.UserData.IsDir(path))
                {
                    if (!TryEnumerateMapFiles(root, path, files, ref entries, depth + 1))
                        return false;

                    continue;
                }

                if (!string.Equals(path.Extension, "yml", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryEnsureNoReparsePoints(path, allowMissingFinal: false))
                    return false;

                files.Add(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        return true;
    }

    private bool TryGetStoredFileSize(ResPath path, ResPath root, out long size)
    {
        size = 0;
        if (!IsPathInside(path, root) || !TryEnsureNoReparsePoints(path, allowMissingFinal: false))
            return false;

        try
        {
            using var stream = _resources.UserData.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            size = stream.Length;
            return size >= 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void CleanupIncompleteUploads(ResPath root)
    {
        var incoming = root / ".incoming";
        try
        {
            foreach (var name in _resources.UserData.DirectoryEntries(incoming))
            {
                if (!IsSafeEntryName(name)
                    || !string.Equals(Path.GetExtension(name), ".part", StringComparison.OrdinalIgnoreCase)
                    || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _))
                {
                    continue;
                }

                var temporary = incoming / name;
                if (_resources.UserData.IsDir(temporary)
                    || !TryEnsureNoReparsePoints(temporary, allowMissingFinal: false))
                {
                    continue;
                }

                _resources.UserData.Delete(temporary);
                _sawmill.Info("Removed incomplete mapper map upload {0} after startup.", name);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _sawmill.Warning("Could not completely clean mapper map temporary uploads: {0}", e.GetType().Name);
        }
    }

    private bool IsUsableDirectory(ResPath directory, ResPath root)
    {
        return IsPathInside(directory, root)
            && GetDirectoryDepth(directory, root) <= MaxFolderDepth
            && _resources.UserData.IsDir(directory)
            && TryEnsureNoReparsePoints(directory, allowMissingFinal: false);
    }

    private static int GetDirectoryDepth(ResPath directory, ResPath root)
    {
        if (!IsPathInside(directory, root))
            return int.MaxValue;

        var relative = directory.RelativeTo(root);
        return relative.IsSelf
            ? 0
            : relative.CanonPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    ///     Creates the configured tree one segment at a time, checking every existing parent before going deeper.
    ///     This avoids following a pre-existing symlink when creating <c>.incoming</c>.
    /// </summary>
    private bool TryCreateTransferDirectories(ResPath root)
    {
        if (_resources.UserData.RootDir is not { } userDataRoot)
            return false;

        try
        {
            var userDataFull = Path.GetFullPath(userDataRoot);
            if (!Directory.Exists(userDataFull)
                || (File.GetAttributes(userDataFull) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            var current = userDataFull;
            var parts = root.CanonPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts.Append(".incoming"))
            {
                current = Path.Combine(current, part);
                if (File.Exists(current) && !Directory.Exists(current))
                    return false;

                if (!Directory.Exists(current))
                    Directory.CreateDirectory(current);

                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return false;
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private bool TryEnsureNoReparsePoints(ResPath path, bool allowMissingFinal)
    {
        if (!TryGetRootDirectoryUnchecked(out var root)
            || !IsPathInside(path, root)
            || _resources.UserData.RootDir is not { } userDataRoot)
        {
            return false;
        }

        try
        {
            var rootFull = Path.GetFullPath(Path.Combine(userDataRoot, root.ToRelativeSystemPath()));
            var targetFull = Path.GetFullPath(Path.Combine(userDataRoot, path.ToRelativeSystemPath()));
            if (!targetFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !string.Equals(targetFull, rootFull, StringComparison.Ordinal))
            {
                return false;
            }

            if (!Directory.Exists(rootFull)
                || (File.GetAttributes(rootFull) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            var relative = path.RelativeTo(root);
            var current = rootFull;
            var parts = relative.IsSelf ? [] : relative.CanonPath.Split('/');
            for (var index = 0; index < parts.Length; index++)
            {
                current = Path.Combine(current, parts[index]);
                if (!File.Exists(current) && !Directory.Exists(current))
                    return allowMissingFinal && index == parts.Length - 1;

                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return false;
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private bool TryGetRootDirectoryUnchecked(out ResPath root)
    {
        root = ResPath.Root;
        var configured = _cfg.GetCVar(CCVars.MapTransferRoot).Trim();
        if (string.IsNullOrEmpty(configured)
            || configured.Length > 512
            || configured.Contains('\\')
            || configured.StartsWith('/'))
            return false;

        var parts = configured.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is 0 or > MaxFolderDepth || parts.Any(part => !ResPath.IsValidFilename(part)))
            return false;

        foreach (var part in parts)
        {
            root /= part;
        }

        return true;
    }

    private static bool IsPathInside(ResPath path, ResPath root)
    {
        return path == root || path.CanonPath.StartsWith(root.CanonPath + '/', StringComparison.Ordinal);
    }

    /// <summary>
    ///     Finds a short server-owned filename and reserves it against pending uploads.
    ///     Names are per directory, so <c>map-1.yml</c> can exist in different mapper folders.
    /// </summary>
    private ResPath? FindFreeUploadedMapPath(ResPath directory)
    {
        try
        {
            for (var number = 1; number <= MaxGeneratedMapNumber; number++)
            {
                var candidate = directory / $"map-{number}.yml";
                if (_resources.UserData.Exists(candidate)
                    || _operations.Values.Any(operation => operation.FinalPath == candidate))
                {
                    continue;
                }

                return candidate;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _sawmill.Warning("Could not allocate a mapper map filename in {0}: {1}", directory, e.GetType().Name);
        }

        return null;
    }

    /// <summary>
    ///     Makes renames portable between Linux (case-sensitive) and Windows (case-insensitive) file systems.
    ///     A case-only rename is deliberately rejected as ambiguous on Windows.
    /// </summary>
    private bool TryHasMapDestinationConflict(ResPath source, ResPath destination, out bool hasConflict)
    {
        hasConflict = false;
        var entries = 0;
        try
        {
            foreach (var name in _resources.UserData.DirectoryEntries(source.Directory))
            {
                if (++entries > MaxDirectoryEntriesToScan)
                    return false;

                if (!string.Equals(name, destination.Filename, StringComparison.OrdinalIgnoreCase))
                    continue;

                var candidate = source.Directory / name;
                if (candidate != source || !string.Equals(name, destination.Filename, StringComparison.Ordinal))
                {
                    hasConflict = true;
                    return true;
                }
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _sawmill.Warning("Could not check mapper map rename destination in {0}: {1}", source.Directory, e.GetType().Name);
            return false;
        }
    }

    /// <summary>
    ///     Validates a mapper-supplied base name. The server appends <c>.yml</c> itself.
    /// </summary>
    internal static bool TryNormalizeMapBaseName(string? requestedName, out string name)
    {
        name = string.Empty;
        if (requestedName == null || requestedName.Length > MaxMapBaseNameLength)
            return false;

        string normalized;
        try
        {
            normalized = requestedName.Trim().Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (normalized.Length is 0 or > MaxMapBaseNameLength
            || Path.HasExtension(normalized)
            || normalized.EndsWith(".", StringComparison.Ordinal)
            || normalized.EndsWith(' ')
            || normalized.IndexOfAny(InvalidMapFileNameCharacters) >= 0
            || !IsSafeEntryName(normalized)
            || IsReservedWindowsFileName(normalized))
        {
            return false;
        }

        name = normalized;
        return true;
    }

    private static bool IsReservedWindowsFileName(string name)
    {
        var stemEnd = name.IndexOf('.');
        var stem = stemEnd < 0 ? name : name[..stemEnd];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4
               && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                   || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
               && stem[3] is >= '1' and <= '9';
    }

    private static bool IsSafeEntryName(string name)
    {
        return name.Length is > 0 and <= 128
            && name[0] != '.'
            && ResPath.IsValidFilename(name)
            && name.All(character => !char.IsControl(character));
    }

    private void DisconnectOnMainThread(INetChannel channel, string reason)
    {
        _tasks.RunOnMainThread(() =>
        {
            if (channel.IsConnected)
                channel.Disconnect(reason);
        });
    }

    private async Task RunOnMainThread(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tasks.RunOnMainThread(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception e)
            {
                completion.TrySetException(e);
            }
        });

        await completion.Task.ConfigureAwait(false);
    }
}
