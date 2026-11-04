using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._WH40K.ShuttleStorage;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;

namespace Content.Server._WH40K.ShuttleStorage;

public sealed partial class ShuttleStorageSystem
{
    public bool Ready => _startup is { IsCompletedSuccessfully: true } && !Busy;
    public Task<Wh40kShuttleSlot?> InspectAccountAsync(Guid owner) => _db.GetWh40kShuttleAsync(owner);

    public Task<ShuttleStorageResult> ResolveAsync(NetUserId owner, string mode, string reason,
        string administrator, EntityUid? console = null)
    {
        if (!Ready || _stopping || string.IsNullOrWhiteSpace(reason) || reason.Length > 512)
            return Task.FromResult(ShuttleStorageResult.Busy);
        var operation = new Transfer(owner, console ?? EntityUid.Invalid, null, 0, Guid.NewGuid(), _epoch);
        _transfer = operation;
        var work = ResolveCoreAsync(operation, mode, reason, administrator);
        _work = work;
        return work;
    }

    private async Task<ShuttleStorageResult> ResolveCoreAsync(Transfer operation, string mode, string reason, string administrator)
    {
        var result = ShuttleStorageResult.RecoveryRequired;
        try
        {
            var row = await ReadConfirmedAsync(operation.Owner.UserId);
            if (row == null) return result = ShuttleStorageResult.Empty;
            if (mode == "recover" && row.State is Wh40kShuttleState.Storing or Wh40kShuttleState.Loading)
            {
                operation.Row = await RecoverRowAsync(row);
                return result = (ShuttleStorageResult) operation.Row.Result;
            }
            if (row.State != Wh40kShuttleState.RecoveryRequired)
                return result = ShuttleStorageResult.Stale;
            var allowed = await OnMain(() =>
            {
                CheckEpoch(operation);
                EntityUid? live = null;
                var ships = EntityManager.AllEntityQueryEnumerator<ShuttleStorageTransferComponent>();
                while (ships.MoveNext(out var entity, out var marker))
                    if (marker.Id == row.Ship) { live = entity; break; }
                if (mode == "restore")
                    return live == null && row.Payload != null && row.Bytes == row.Payload.Length &&
                           row.Sha256 is { Length: 32 } && row.Schema == ShuttleStorageLimits.Schema &&
                           ShuttleStorageLimits.FitsDimensions(row.Width, row.Height) &&
                           _serializer.Validate(row.Payload, row.Sha256);
                if (mode == "discard")
                    return live == null || !Comp<ShuttleStorageTransferComponent>(live.Value).Active;
                if (mode != "return" || live == null || !Comp<ShuttleStorageTransferComponent>(live.Value).Active ||
                    Transform(live.Value).MapUid is not { } map || !HasComp<ShuttleStorageStagingComponent>(map) ||
                    !Facility(operation.Console, out _, out operation.Facility))
                    return false;
                operation.Grid = live.Value;
                operation.Staging = map;
                operation.Isolated = true;
                if (!StagingContainsOnlyGrid(operation)) return false;
                var berth = GetBerth(operation);
                if (berth == null) return false;
                _shuttles.FTLDock((operation.Grid, Transform(operation.Grid)), berth);
                operation.Isolated = false;
                Comp<ShuttleStorageTransferComponent>(operation.Grid).Active = false;
                DeleteStaging(operation);
                return true;
            });
            if (!allowed) return result = ShuttleStorageResult.Denied;
            var restore = mode == "restore";
            result = restore ? ShuttleStorageResult.Stored : ShuttleStorageResult.Empty;
            var applied = false;
            try { applied = await _db.ResolveWh40kShuttleAsync(row.Owner, row.Revision, _epoch, restore, (int) result); }
            catch (Exception)
            {
                var actual = await ReadConfirmedAsync(row.Owner);
                applied = actual?.Revision == row.Revision + 1 && actual.State ==
                    (restore ? Wh40kShuttleState.Stored : Wh40kShuttleState.Empty);
            }
            if (!applied) return result = ShuttleStorageResult.Stale;
            operation.Row = await ReadConfirmedAsync(row.Owner);
            Log.Info($"Administrator {administrator} resolved shuttle {row.Ship}, account {row.Owner}, mode {mode}: {reason}");
        }
        catch (Exception error)
        {
            Log.Warning($"Administrative shuttle recovery failed: {error.Message}");
            result = ShuttleStorageResult.RecoveryRequired;
        }
        finally
        {
            if (!_shutdown.IsCancellationRequested)
                await OnMain(() =>
                {
                    SetResult(operation.Owner, result, operation.Row);
                    _transfer = null;
                    Publish();
                    return true;
                });
        }
        return result;
    }
}
