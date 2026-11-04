using System.Threading.Tasks;
using Content.Shared._WH40K.ShuttleStorage;

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

    private async Task<ShuttleStorageSerializer.CaptureSession> CaptureAsync(Transfer operation, bool settle = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            var capture = await CaptureOnceAsync(operation);
            // Native power/node cleanup can finish after the map transition. Before authorization,
            // resample the same isolated original instead of committing an inconsistent snapshot.
            if (!settle || !capture.Changed || attempt >= 2)
                return capture;
            await NextCaptureFrameAsync(2);
        }
    }

    private async Task<ShuttleStorageSerializer.CaptureSession> CaptureOnceAsync(Transfer operation)
    {
        var capture = await OnMain(() =>
        {
            CheckEpoch(operation);
            if (!StagingContainsOnlyGrid(operation))
                throw new InvalidOperationException("Unexpected entities on the shuttle staging map.");
            return _serializer.BeginCapture(operation.Grid);
        });
        while (!await OnMain(() => { CheckEpoch(operation); return _serializer.AdvanceCapture(capture); }))
            await NextCaptureFrameAsync();
        if (capture.Result != ShuttleStorageResult.Ready)
            return capture;
        var encoded = await Task.Run(() => ShuttleStorageSerializer.EncodeCapture(capture));
        await OnMain(() =>
        {
            CheckEpoch(operation);
            capture.Result = encoded.Result;
            if (encoded.Result == ShuttleStorageResult.Ready)
            {
                capture.Changed = !_serializer.CaptureStillCurrent(capture);
                if (encoded.Parsed == null || !_serializer.ValidateNode(encoded.Parsed) || capture.Changed)
                    capture.Result = ShuttleStorageResult.Unsupported;
            }
            if (capture.Result == ShuttleStorageResult.Ready)
                capture.Payload = encoded.Payload;
            return true;
        });
        return capture;
    }

    private async Task DeleteSourceAsync(Transfer operation)
    {
        if (await OnMain(() => Exists(operation.Grid)))
        {
            var verification = await CaptureAsync(operation);
            if (verification.Result != ShuttleStorageResult.Ready || operation.Row?.Payload is not { } expected)
                throw new InvalidOperationException($"Shuttle verification failed: {verification.Result}; {_serializer.LastInspectionFailure}");
            var difference = await Task.Run(() => _serializer.Equivalent(expected, verification.Payload!, out var detail) ? null : detail);
            if (difference != null)
                throw new InvalidOperationException($"The isolated shuttle changed after its snapshot was committed: {difference}");
            await OnMain(() =>
            {
                CheckEpoch(operation);
                if (!_serializer.CaptureStillCurrent(verification))
                    throw new InvalidOperationException("The source shuttle changed during snapshot encoding.");
                DeleteSource(operation);
                return true;
            });
        }
        else await OnMain(() => { DeleteSource(operation); return true; });
    }
}
