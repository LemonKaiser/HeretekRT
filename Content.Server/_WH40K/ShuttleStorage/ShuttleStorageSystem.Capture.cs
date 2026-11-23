using System.Threading.Tasks;
using Content.Shared._WH40K.ShuttleStorage;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Server._WH40K.ShuttleStorage;

public sealed partial class ShuttleStorageSystem
{
    private async Task NextCaptureFrameAsync(uint ticks = 1)
    {
        var frame = await OnMain(() =>
        {
            if (_stopping)
                return Task.CompletedTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _frameWait = (_timing.CurTick.Value + ticks, completion);
            return (Task) completion.Task;
        });
        await frame.WaitAsync(_shutdown.Token);
    }

    private async Task<(ShuttleStorageResult Result, ShuttleStorageInspection Info, byte[]? Payload, MappingDataNode? Node)> CaptureAsync(Transfer operation)
    {
        var capture = await OnMain(() =>
        {
            CheckEpoch(operation);
            if (!StagingContainsOnlyGrid(operation))
                throw new InvalidOperationException("Unexpected entities on the shuttle staging map.");
            var result = _serializer.Capture(operation.Grid, out var info, out var node);
            return (result, info, node);
        });
        if (capture.result != ShuttleStorageResult.Ready)
            return (capture.result, capture.info, null, null);

        var encoded = await Task.Run(() => ShuttleStorageSerializer.EncodeCapture(capture.node!));
        return (encoded.Result, capture.info, encoded.Payload, capture.node);
    }

    private Task DeleteSourceAsync(Transfer operation) => OnMain(() =>
    {
        CheckEpoch(operation);
        if (Exists(operation.Grid))
        {
            if (operation.Row?.Payload == null || operation.Snapshot is not { } expected)
                throw new InvalidOperationException("Missing committed shuttle snapshot.");
            if (!StagingContainsOnlyGrid(operation))
                throw new InvalidOperationException("Unexpected entities on the shuttle staging map.");
            var result = _serializer.Capture(operation.Grid, out _, out var current);
            if (result != ShuttleStorageResult.Ready)
                throw new InvalidOperationException($"Shuttle verification failed: {result}; {_serializer.LastInspectionFailure}");
            if (ShuttleStorageSerializer.SnapshotDifference(expected, current!) is { } difference)
                throw new InvalidOperationException($"The isolated shuttle changed after its snapshot was committed: {difference}");
        }

        DeleteSource(operation);
        return true;
    });
}
