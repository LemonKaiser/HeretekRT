using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Shuttles;
using Content.Shared._WH40K.ShuttleStorage;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;

namespace Content.Server._WH40K.ShuttleStorage;

public sealed partial class ShuttleStorageSystem
{
    private sealed class Transfer(NetUserId owner, EntityUid console, EntityUid? grid, long revision, Guid request, Guid epoch)
    {
        public readonly NetUserId Owner = owner;
        public readonly EntityUid Console = console;
        public readonly long ExpectedRevision = revision;
        public readonly Guid Request = request;
        public readonly Guid Epoch = epoch;
        public readonly bool Deposit = grid != null;
        public EntityUid Grid = grid ?? EntityUid.Invalid;
        public EntityUid? Staging;
        public EntityUid Facility;
        public bool Isolated;
        public bool Published;
        public bool PlacementCompleted;
        public Wh40kShuttleSlot? Row;
    }

    // Server-side business entry point. Network requests additionally pass CanUse and actor/UI membership checks.
    public Task<ShuttleStorageResult> TransferAsync(NetUserId owner, EntityUid console, EntityUid? ship,
        long revision, Guid request)
    {
        ShuttleStorageResult? rejection = null;
        if (_stopping || !_config.GetCVar(ShuttleStorageCVars.Enabled))
            rejection = ShuttleStorageResult.Disabled;
        else if (request == Guid.Empty || owner.UserId == Guid.Empty || !Facility(console, out _, out _))
            rejection = ShuttleStorageResult.Denied;
        else if (_transfer != null || _startup is not { IsCompletedSuccessfully: true })
            rejection = ShuttleStorageResult.ServiceBusy;
        else if (_views.TryGetValue(owner, out var cached) && cached.Row is { } previous && previous.Operation == request)
            rejection = (ShuttleStorageResult) previous.Result;
        else if (_cooldowns.TryGetValue(owner, out var deadline) && deadline > _timing.CurTime)
            rejection = ShuttleStorageResult.Cooldown;
        if (rejection is { } result)
        {
            SetResult(owner, result);
            return Task.FromResult(result);
        }

        _cooldowns[owner] = _timing.CurTime + TimeSpan.FromSeconds(Math.Clamp(_config.GetCVar(ShuttleStorageCVars.Cooldown), 0, 300));
        var operation = new Transfer(owner, console, ship, revision, request, _epoch);
        _transfer = operation;
        SetResult(owner, ShuttleStorageResult.Busy);
        var work = RunTransferAsync(operation);
        _work = work;
        return work;
    }

    private async Task<ShuttleStorageResult> RunTransferAsync(Transfer operation)
    {
        var result = ShuttleStorageResult.Unavailable;
        try
        {
            var row = await _db.GetWh40kShuttleAsync(operation.Owner.UserId, payload: true);
            operation.Row = row;
            await OnMain(() => { CheckEpoch(operation); return true; });
            if (row?.Operation == operation.Request)
                return result = (ShuttleStorageResult) row.Result;
            if ((row?.Revision ?? 0) != operation.ExpectedRevision)
                return result = ShuttleStorageResult.Stale;
            if (operation.Deposit && row is { State: not Wh40kShuttleState.Empty })
                return result = row.State == Wh40kShuttleState.RecoveryRequired ? ShuttleStorageResult.RecoveryRequired : ShuttleStorageResult.Full;
            if (!operation.Deposit && row?.State != Wh40kShuttleState.Stored)
                return result = row?.State == Wh40kShuttleState.RecoveryRequired ? ShuttleStorageResult.RecoveryRequired : ShuttleStorageResult.Empty;
            if (!operation.Deposit && !await OnMain(() => { CheckEpoch(operation); return ValidStoredSnapshot(row!) && !HasLiveShip(row!.Ship); }))
            {
                // Validate before reserving Loading: the repository deliberately refuses a normal
                // transition carrying corrupt bytes. Quarantine preserves the original BLOB verbatim.
                var corrupt = row!.Copy();
                corrupt.State = Wh40kShuttleState.RecoveryRequired;
                corrupt.Result = (int) ShuttleStorageResult.Corrupt;
                operation.Row = await CommitAsync(row, corrupt);
                return result = ShuttleStorageResult.Corrupt;
            }

            var next = row?.Copy() ?? new Wh40kShuttleSlot { Owner = operation.Owner.UserId };
            next.Operation = operation.Request;
            next.Epoch = operation.Epoch;
            next.Authorized = false;
            next.Result = (int) ShuttleStorageResult.Busy;
            next.State = operation.Deposit ? Wh40kShuttleState.Storing : Wh40kShuttleState.Loading;
            if (operation.Deposit)
            {
                var admission = await OnMain(() => Inspect(operation.Console, operation.Owner, operation.Grid, out _));
                if (admission != ShuttleStorageResult.Ready)
                    return result = admission;
                await OnMain(() =>
                {
                    CheckEpoch(operation);
                    next.Ship = EnsureComp<ShuttleStorageTransferComponent>(operation.Grid).Id;
                    return true;
                });
                ClearPayload(next);
            }
            operation.Row = await CommitAsync(row, next);
            await OnMain(() => { CheckEpoch(operation); return true; });
            result = operation.Deposit ? await DepositAsync(operation) : await WithdrawAsync(operation);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception error)
        {
            Log.Warning($"Shuttle transfer {operation.Request} interrupted: {error.Message}");
            try { result = await AbortAsync(operation); }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            catch (Exception recoveryError)
            {
                Log.Error($"Shuttle transfer {operation.Request} needs recovery: {recoveryError}");
                result = ShuttleStorageResult.RecoveryRequired;
            }
        }
        finally
        {
            if (!_shutdown.IsCancellationRequested)
            {
                await OnMain(() =>
                {
                    if (operation.Epoch == _epoch)
                        SetResult(operation.Owner, result, operation.Row);
                    if (ReferenceEquals(_transfer, operation))
                        _transfer = null;
                    Publish();
                    return true;
                });
            }
        }
        return result;
    }

    private async Task<ShuttleStorageResult> DepositAsync(Transfer operation)
    {
        var admission = await OnMain(() =>
        {
            CheckEpoch(operation);
            var status = Inspect(operation.Console, operation.Owner, operation.Grid, out _);
            if (status != ShuttleStorageResult.Ready)
                return status;
            Facility(operation.Console, out _, out operation.Facility);
            operation.Staging = _maps.CreateMap(out var mapId);
            EnsureComp<ShuttleStorageStagingComponent>(operation.Staging.Value);
            _maps.SetPaused(mapId, true);
            EnsureComp<ShuttleStorageTransferComponent>(operation.Grid).Active = true;
            _docking.UndockDocks(operation.Grid);
            if (_docking.GetDocks(operation.Grid).Any(dock => dock.Comp.Docked))
                throw new InvalidOperationException("The shuttle could not be disconnected safely.");
            try { _transform.SetCoordinates(operation.Grid, new EntityCoordinates(operation.Staging.Value, 0, 0)); }
            finally { operation.Isolated = Transform(operation.Grid).MapUid == operation.Staging; }
            CheckEpoch(operation);
            return ShuttleStorageResult.Ready;
        });
        if (admission != ShuttleStorageResult.Ready)
            return await CancelDepositAsync(operation, admission);

        // Let native undocking, physics and node-group cleanup finish before sampling the paused grid.
        await NextCaptureFrameAsync(2);
        var snapshot = await CaptureAsync(operation, settle: true);
        if (snapshot.Result != ShuttleStorageResult.Ready)
            return await CancelDepositAsync(operation, snapshot.Result);
        var captured = await OnMain(() =>
        {
            CheckEpoch(operation);
            if (!StagingContainsOnlyGrid(operation))
                throw new InvalidOperationException("The isolated shuttle split or left entities outside its grid.");
            var payload = snapshot.Payload!;
            var info = snapshot.Info;
            var next = operation.Row!.Copy();
            next.Payload = payload;
            next.Sha256 = SHA256.HashData(payload!);
            next.Bytes = payload!.Length;
            next.Width = info.Width;
            next.Height = info.Height;
            next.Entities = info.Entities.Count;
            next.Schema = ShuttleStorageLimits.Schema;
            next.Name = Name(operation.Grid);
            next.SavedAt = DateTime.UtcNow;
            next.Authorized = true;
            CheckEpoch(operation);
            return next;
        });
        operation.Row = await CommitAsync(operation.Row, captured);
        await OnMain(() => { CheckEpoch(operation); return true; });
        await DeleteSourceAsync(operation);
        await OnMain(() =>
        {
            CheckEpoch(operation);
            return true;
        });
        var stored = operation.Row.Copy();
        stored.State = Wh40kShuttleState.Stored;
        stored.Authorized = false;
        stored.Result = (int) ShuttleStorageResult.Stored;
        operation.Row = await CommitAsync(operation.Row, stored);
        await OnMain(() => { CheckEpoch(operation); return true; });
        return ShuttleStorageResult.Stored;
    }

    private async Task<ShuttleStorageResult> WithdrawAsync(Transfer operation)
    {
        var admission = await OnMain(() =>
        {
            CheckEpoch(operation);
            if (_stopping)
                return ShuttleStorageResult.Unavailable;
            if (!Facility(operation.Console, out _, out operation.Facility))
                return ShuttleStorageResult.NoBerth;
            var row = operation.Row!;
            if (!ValidStoredSnapshot(row) || HasLiveShip(row.Ship))
                return ShuttleStorageResult.Corrupt;
            operation.Staging = _maps.CreateMap(out var mapId);
            EnsureComp<ShuttleStorageStagingComponent>(operation.Staging.Value);
            _maps.SetPaused(mapId, true);
            if (!_serializer.TryLoad(mapId, row.Payload!, out operation.Grid))
                return ShuttleStorageResult.Unsupported;
            operation.Isolated = true;
            if (!StagingContainsOnlyGrid(operation))
                return ShuttleStorageResult.Unsupported;
            var marker = EnsureComp<ShuttleStorageTransferComponent>(operation.Grid);
            marker.Active = true;
            marker.ShipId = row.Ship.ToString("N");
            CheckEpoch(operation);
            return GetBerth(operation) != null ? ShuttleStorageResult.Ready : ShuttleStorageResult.NoBerth;
        });
        if (admission != ShuttleStorageResult.Ready)
            return await CancelWithdrawalAsync(operation, admission);
        var authorized = operation.Row!.Copy();
        authorized.Authorized = true;
        operation.Row = await CommitAsync(operation.Row, authorized);
        DockingConfig? reserved = null;
        Task frame = Task.CompletedTask;
        var published = await OnMain(() =>
        {
            CheckEpoch(operation);
            if (_stopping)
                return false;
            var berth = GetBerth(operation);
            if (berth == null)
                return false;
            _shipyard.RestoreStoredShuttle(operation.Grid, operation.Owner,
                _players.TryGetSessionById(operation.Owner, out var session) ? session : null);
            reserved = berth;
            try
            {
                var coordinates = _transform.ToMapCoordinates(berth.Coordinates);
                _transform.SetCoordinates(operation.Grid, Transform(operation.Grid), new EntityCoordinates(_maps.GetMap(coordinates.MapId), coordinates.Position),
                    rotation: berth.Angle + _transform.GetWorldRotation(berth.Coordinates.EntityId));
            }
            finally
            {
                if (Exists(operation.Grid) && Transform(operation.Grid).MapUid != operation.Staging)
                {
                    operation.Published = true;
                    operation.Isolated = false;
                    Comp<ShuttleStorageTransferComponent>(operation.Grid).Active = false;
                    DeleteStaging(operation);
                }
            }
            // Send the new grid before creating its cross-grid joint. Otherwise a connected client
            // may apply the existing station's joint state before the new grid has been initialized.
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _frameWait = (_timing.CurTick.Value + 2, ready);
            frame = ready.Task;
            CheckEpoch(operation);
            return true;
        });
        if (!published)
            return await QuarantineAsync(operation);
        await frame.WaitAsync(_shutdown.Token);
        var docked = await OnMain(() =>
        {
            CheckEpoch(operation);
            if (_stopping)
                return false;
            if (reserved == null || !BerthStillValid(operation, reserved))
                return false;
            _shuttles.FTLDock((operation.Grid, Transform(operation.Grid)), reserved);
            operation.PlacementCompleted = true;
            CheckEpoch(operation);
            return true;
        });
        if (!docked)
            return await QuarantineAsync(operation);
        var empty = operation.Row.Copy();
        empty.State = Wh40kShuttleState.Empty;
        empty.Authorized = false;
        empty.Result = (int) ShuttleStorageResult.Retrieved;
        ClearPayload(empty);
        operation.Row = await CommitAsync(operation.Row, empty);
        await OnMain(() => { CheckEpoch(operation); return true; });
        return ShuttleStorageResult.Retrieved;
    }

    private bool ValidStoredSnapshot(Wh40kShuttleSlot row) => row.Schema == ShuttleStorageLimits.Schema &&
        row.Ship != Guid.Empty && ShuttleStorageLimits.FitsDimensions(row.Width, row.Height) && row.Payload != null &&
        row.Bytes == row.Payload.Length && row.Sha256 is { Length: 32 } && _serializer.Validate(row.Payload, row.Sha256);

    private void CheckEpoch(Transfer operation)
    {
        if (operation.Epoch != _epoch)
            throw new InvalidOperationException("Shuttle operation belongs to the previous round.");
    }

    private void SetResult(NetUserId owner, ShuttleStorageResult result, Wh40kShuttleSlot? row = null)
    {
        if (!_views.TryGetValue(owner, out var view))
            _views[owner] = view = new View();
        if (row != null)
        {
            view.Row = row.Copy();
            view.Row.Payload = null;
            view.Read = null;
        }
        view.Result = result;
        view.ViewedAt = _timing.CurTime;
    }

    private static void ClearPayload(Wh40kShuttleSlot row)
    {
        row.Payload = null;
        row.Sha256 = null;
        row.Bytes = 0;
    }

    private void DeleteStaging(Transfer operation)
    {
        if (operation.Staging is { } map && Exists(map))
        {
            if (!HasComp<ShuttleStorageStagingComponent>(map) ||
                operation.Deposit && Transform(map).ChildCount != 0)
                throw new InvalidOperationException("Refusing to delete a staging map with unaccounted source entities.");
            Del(map);
        }
        operation.Staging = null;
    }

    private bool StagingContainsOnlyGrid(Transfer operation)
    {
        if (operation.Staging is not { } map || !Exists(map) || !HasComp<ShuttleStorageStagingComponent>(map))
            return false;
        var children = Transform(map).ChildEnumerator;
        var found = false;
        while (children.MoveNext(out var child))
        {
            if (child != operation.Grid) return false;
            found = true;
        }
        return found;
    }

    private void DeleteSource(Transfer operation)
    {
        CheckEpoch(operation);
        if (Exists(operation.Grid))
        {
            if (!operation.Isolated || !Comp<ShuttleStorageTransferComponent>(operation.Grid).Active ||
                Transform(operation.Grid).MapUid != operation.Staging || operation.Staging is not { } staging ||
                !TryComp(staging, out MapComponent? map) || !_maps.IsPaused(map.MapId) || !StagingContainsOnlyGrid(operation))
                throw new InvalidOperationException("Refusing to delete an accessible shuttle.");
            _shipyard.RemoveStoredShuttleRegistrations(operation.Grid);
            Del(operation.Grid);
        }
        operation.Isolated = false;
        DeleteStaging(operation);
    }

    private DockingConfig? GetBerth(Transfer operation)
    {
        if (!Exists(operation.Grid) || !Exists(operation.Facility) || !HasComp<MapGridComponent>(operation.Facility))
            return null;
        var config = _docking.GetDockingConfig(operation.Grid, operation.Facility);
        if (config == null)
        {
            Log.Debug($"No docking configuration for {ToPrettyString(operation.Grid)}; " +
                string.Join(", ", _docking.GetDocks(operation.Grid).Select(port =>
                    $"{port.Owner}: docked={port.Comp.Docked}, anchored={Transform(port.Owner).Anchored}, type={port.Comp.DockType}")) +
                "; target: " + string.Join(", ", _docking.GetDocks(operation.Facility).Select(port =>
                    $"{port.Owner}: docked={port.Comp.Docked}, anchored={Transform(port.Owner).Anchored}, type={port.Comp.DockType}")));
        }
        return config != null && FitsBerth(operation.Grid, config) ? config : null;
    }
}
