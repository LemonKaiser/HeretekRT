using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._WH40K.ShuttleStorage;
using Robust.Shared.Network;

namespace Content.Server._WH40K.ShuttleStorage;

public sealed partial class ShuttleStorageSystem
{
    private async Task<Wh40kShuttleSlot> CommitAsync(Wh40kShuttleSlot? before, Wh40kShuttleSlot next)
    {
        next.Revision = (before?.Revision ?? 0) + 1;
        Exception? failure = null;
        try
        {
            if (await _db.WriteWh40kShuttleAsync(next, before?.Revision ?? 0))
                return next;
        }
        catch (Exception error) { failure = error; }

        // A failed/late response is not evidence that the transaction was rolled back.
        var actual = await ReadConfirmedAsync(next.Owner);
        if (actual?.MatchesTransition(next) == true)
            return actual;
        throw new IOException("Shuttle storage transition was not committed at the expected revision.", failure);
    }

    private async Task<Wh40kShuttleSlot?> ReadConfirmedAsync(Guid owner)
    {
        var logged = false;
        while (true)
        {
            _shutdown.Token.ThrowIfCancellationRequested();
            try { return await _db.GetWh40kShuttleAsync(owner, payload: true); }
            catch (Exception error)
            {
                if (!logged)
                {
                    Log.Warning($"Waiting for a definitive shuttle database result for {owner}: {error.Message}");
                    logged = true;
                }
                await Task.Delay(1000, _shutdown.Token);
            }
        }
    }

    private async Task<ShuttleStorageResult> CancelDepositAsync(Transfer operation, ShuttleStorageResult reason)
    {
        var returned = await OnMain(() =>
        {
            CheckEpoch(operation);
            if (!operation.Isolated)
            {
                if (TryComp<ShuttleStorageTransferComponent>(operation.Grid, out var marker))
                    marker.Active = false;
                DeleteStaging(operation);
                return true;
            }
            if (!StagingContainsOnlyGrid(operation))
                return false;
            var berth = GetBerth(operation);
            if (berth == null)
                return false;
            _shuttles.FTLDock((operation.Grid, Transform(operation.Grid)), berth);
            Comp<ShuttleStorageTransferComponent>(operation.Grid).Active = false;
            operation.Isolated = false;
            DeleteStaging(operation);
            return true;
        });
        if (!returned)
            return await QuarantineAsync(operation);
        var empty = operation.Row!.Copy();
        empty.State = Wh40kShuttleState.Empty;
        empty.Result = (int) reason;
        ClearPayload(empty);
        operation.Row = await CommitAsync(operation.Row, empty);
        return reason;
    }

    private async Task<ShuttleStorageResult> CancelWithdrawalAsync(Transfer operation, ShuttleStorageResult reason)
    {
        if (operation.Row!.Authorized)
            return await QuarantineAsync(operation);
        await OnMain(() =>
        {
            CheckEpoch(operation);
            DeleteStaging(operation);
            operation.Isolated = false;
            return true;
        });
        var stored = operation.Row.Copy();
        stored.State = reason is ShuttleStorageResult.Corrupt or ShuttleStorageResult.Unsupported
            ? Wh40kShuttleState.RecoveryRequired : Wh40kShuttleState.Stored;
        stored.Result = (int) reason;
        operation.Row = await CommitAsync(operation.Row, stored);
        return reason;
    }

    private async Task<ShuttleStorageResult> QuarantineAsync(Transfer operation)
    {
        if (!operation.Deposit && !operation.Published && operation.Epoch == _epoch)
        {
            await OnMain(() =>
            {
                if (Exists(operation.Grid))
                    _shipyard.RemoveStoredShuttleRegistrations(operation.Grid);
                DeleteStaging(operation);
                operation.Isolated = false;
                return true;
            });
        }
        var quarantine = operation.Row!.Copy();
        quarantine.State = Wh40kShuttleState.RecoveryRequired;
        quarantine.Result = (int) ShuttleStorageResult.RecoveryRequired;
        operation.Row = await CommitAsync(operation.Row, quarantine);
        return ShuttleStorageResult.RecoveryRequired;
    }

    private async Task<ShuttleStorageResult> AbortAsync(Transfer operation)
    {
        operation.Row = await ReadConfirmedAsync(operation.Owner.UserId);
        if (operation.Row == null || operation.Row.Operation != operation.Request)
            return ShuttleStorageResult.Unavailable;
        if (operation.Epoch != _epoch)
        {
            operation.Row = await RecoverRowAsync(operation.Row);
            return (ShuttleStorageResult) operation.Row.Result;
        }
        switch (operation.Row.State)
        {
            case Wh40kShuttleState.Storing when !operation.Row.Authorized:
                return await CancelDepositAsync(operation, ShuttleStorageResult.Unavailable);
            case Wh40kShuttleState.Storing:
                try
                {
                    await DeleteSourceAsync(operation);
                }
                catch (Exception error)
                {
                    Log.Warning($"Source shuttle cleanup needs inspection: {error.Message}");
                    return await QuarantineAsync(operation);
                }
                var stored = operation.Row.Copy();
                stored.State = Wh40kShuttleState.Stored;
                stored.Authorized = false;
                stored.Result = (int) ShuttleStorageResult.Stored;
                operation.Row = await CommitAsync(operation.Row, stored);
                return ShuttleStorageResult.Stored;
            case Wh40kShuttleState.Loading when !operation.Row.Authorized:
                return await CancelWithdrawalAsync(operation, ShuttleStorageResult.Unavailable);
            case Wh40kShuttleState.Loading when operation.Published && operation.PlacementCompleted:
                var empty = operation.Row.Copy();
                empty.State = Wh40kShuttleState.Empty;
                empty.Authorized = false;
                empty.Result = (int) ShuttleStorageResult.Retrieved;
                ClearPayload(empty);
                operation.Row = await CommitAsync(operation.Row, empty);
                return ShuttleStorageResult.Retrieved;
            case Wh40kShuttleState.Loading:
                return await QuarantineAsync(operation);
            default:
                return (ShuttleStorageResult) operation.Row.Result;
        }
    }

    private async Task RecoverPendingAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                var rows = await _db.GetPendingWh40kShuttlesAsync();
                foreach (var metadata in rows)
                {
                    var row = await ReadConfirmedAsync(metadata.Owner);
                    if (row is { State: Wh40kShuttleState.Storing or Wh40kShuttleState.Loading })
                        await RecoverRowAsync(row);
                }
                return;
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                Log.Warning($"Shuttle startup recovery could not complete: {error.Message}");
                await Task.Delay(1000, _shutdown.Token);
            }
        }
    }

    public async Task<Wh40kShuttleSlot> RecoverRowAsync(Wh40kShuttleSlot row)
    {
        if (row.State is not (Wh40kShuttleState.Storing or Wh40kShuttleState.Loading))
            return row;
        var next = row.Copy();
        next.Epoch = _epoch;
        var safePayload = row.Payload != null && row.Bytes == row.Payload.Length && row.Sha256 is { Length: 32 } &&
            row.Schema == ShuttleStorageLimits.Schema &&
            await OnMain(() => _serializer.Validate(row.Payload, row.Sha256) && !HasLiveShip(row.Ship));
        if (row.State == Wh40kShuttleState.Storing && row.Authorized && safePayload ||
            row.State == Wh40kShuttleState.Loading && !row.Authorized && safePayload)
        {
            next.State = Wh40kShuttleState.Stored;
            next.Authorized = false;
            next.Result = (int) ShuttleStorageResult.Stored;
        }
        else if (row.State == Wh40kShuttleState.Storing && !row.Authorized &&
                 !await OnMain(() => HasLiveShip(row.Ship)))
        {
            next.State = Wh40kShuttleState.Empty;
            ClearPayload(next);
            next.Result = (int) ShuttleStorageResult.Unavailable;
        }
        else
        {
            next.State = Wh40kShuttleState.RecoveryRequired;
            next.Result = (int) ShuttleStorageResult.RecoveryRequired;
        }
        return await CommitAsync(row, next);
    }

    private bool HasLiveShip(Guid id)
    {
        var ships = EntityManager.AllEntityQueryEnumerator<ShuttleStorageTransferComponent>();
        while (ships.MoveNext(out _, out var marker))
        {
            if (marker.Id == id)
                return true;
        }
        return false;
    }
}
