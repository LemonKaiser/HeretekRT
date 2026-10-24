using System.Linq;
using System.Threading.Tasks;
using Content.Server._WH40K.Administration;
using Content.Server._WH40K.FactionStorage;
using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Server._WH40K.Quests;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server._WH40K.Localizations;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared._WH40K.PersistentInventory;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Eui;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;

namespace Content.Server._WH40K.PersistentInventory;

public sealed partial class PersistentInventoryAdminEui : BaseEui
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IAdminAuthorizationManager _authorization = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IAdminLogManager _logs = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private PersistentInventoryManager _inventory = default!;

    private PersistentInventoryAdminEuiState _view = new();
    private NetUserId? _target;
    private string _username = string.Empty;
    private bool _busy;
    private bool _closed;
    private DateTime _nextRequest;

    public PersistentInventoryAdminEui()
    {
        IoCManager.InjectDependencies(this);
    }

    private bool Authorized => !_closed && !IsShutDown && Player.Status != SessionStatus.Disconnected &&
                               _admins.GetAdminData(Player)?.HasFlag(AdminFlags.Admin) == true;

    public override EuiStateBase GetNewState()
    {
        if (!Authorized)
            return new PersistentInventoryAdminEuiState();
        _view.Busy = _busy;
        return _view;
    }

    public override void Opened()
    {
        _admins.OnPermsChanged += OnPermsChanged;
    }

    public override void Closed()
    {
        _closed = true;
        _admins.OnPermsChanged -= OnPermsChanged;
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player && !Authorized)
            Close();
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (msg is CloseEuiMessage)
            return;
        if (!Authorized)
        {
            Close();
            return;
        }
        switch (msg)
        {
            case PersistentInventoryAdminSelect select:
                SelectPlayer(select.Target);
                break;
            case PersistentInventoryAdminInspect inspect when _target?.UserId == inspect.UserId:
                _ = RunAsync(async () =>
                {
                    if (await AuthorizeTargetAsync())
                        await InspectAsync(inspect.Snapshot);
                });
                break;
            case PersistentInventoryAdminRequest request when _target?.UserId == request.UserId:
                _ = RunAsync(() => ActAsync(request));
                break;
        }
    }

    public void SelectPlayer(string target)
    {
        if (target.Length is < 1 or > 128)
            return;
        _ = RunAsync(async () =>
        {
            var located = await _locator.LookupIdByNameOrIdAsync(target.Trim());
            if (!Authorized)
                return;
            if (located == null)
            {
                _view.Message = Text("persistentinv-admin-player-missing");
                return;
            }
            if (await _authorization.TryDenyTargetAsync(Player, located.UserId, AdminOperation.GenericTarget,
                    located.Username, message => _view.Message = message) || !Authorized)
                return;
            var previous = _target == located.UserId ? _view.SelectedSnapshot : null;
            _target = located.UserId;
            _username = located.Username;
            await ReloadAsync(previous);
        });
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (!Authorized || _busy || DateTime.UtcNow < _nextRequest)
        {
            Dirty();
            return;
        }
        _busy = true;
        Dirty();
        try
        {
            await operation();
        }
        catch (Exception error)
        {
            _view.Message = Text("persistentinv-admin-error", ("error", error.GetType().Name));
            _logManager.GetSawmill("wh40k.persistent_inventory.admin").Error($"Persistent inventory admin panel failed: {error}");
        }
        finally
        {
            _busy = false;
            _nextRequest = DateTime.UtcNow.AddMilliseconds(250);
            Dirty();
        }
    }

    private void Dirty()
    {
        if (!_closed && !IsShutDown)
            StateDirty();
    }

    private async Task<bool> AuthorizeTargetAsync()
    {
        return Authorized && _target is { } user &&
               !await _authorization.TryDenyTargetAsync(Player, user, AdminOperation.GenericTarget,
                   _username, message => _view.Message = message) && Authorized;
    }

    private bool TargetBusy(NetUserId user)
    {
        return _entities.System<Wh40kPlayerResetSystem>().IsResetting(user) ||
               _entities.System<Wh40kQuestSessionSystem>().IsStopping ||
               _entities.System<FactionStorageSystem>().IsBusy(user) ||
               _entities.System<Wh40kQuestSubmissionSystem>().IsBusy(user) ||
               _entities.System<Content.Server._WH40K.Merchant.MerchantSystem>().IsSelling(user) ||
               _inventory.IsLifeLossPending(user) ||
               _players.TryGetSessionById(user, out var session) && session.AttachedEntity is { } body &&
               _entities.HasComponent<PersistentInventoryOperationComponent>(body);
    }

    private async Task ReloadAsync(Guid? selected, string message = "")
    {
        if (_target is not { } user)
            return;
        var header = await _db.GetPersistentInventoryHeaderAsync(user);
        var revisions = await _db.GetPersistentInventoryRevisionsAsync(user);
        var history = await _db.GetPersistentInventoryAuditAsync(user, 100);
        var lost = await _db.GetLatestPersistentInventoryLostSnapshotAsync(user);
        var usage = await _db.GetPersistentInventoryStorageUsageAsync(user);
        var totals = await _db.GetPersistentInventoryStorageTotalsAsync();
        if (!await AuthorizeTargetAsync())
            return;

        var busy = TargetBusy(user);
        var hasBody = _players.TryGetSessionById(user, out var session) && session.AttachedEntity is { } body &&
                      _entities.EntityExists(body);
        var canSelect = !busy && header != null && header.State is not (PersistentInventorySnapshotState.Bound or PersistentInventorySnapshotState.Staging) &&
                        header.VerifiedState != PersistentInventorySnapshotState.Bound &&
                        !_entities.System<PersistentInventoryLifecycleSystem>().HasActiveBoundBody(user);
        _view = new PersistentInventoryAdminEuiState
        {
            UserId = user.UserId,
            PlayerName = _username,
            Revision = header?.Revision.Value ?? 0,
            Overview = DescribeOverview(user, header, busy) + "\n" + Text("persistentinv-storage-usage",
                ("bytes", usage.EstimatedBytes), ("limit", PersistentInventoryStoragePolicy.AccountBytes),
                ("payload", usage.PayloadBytes), ("snapshots", usage.Snapshots), ("blobs", usage.Blobs),
                ("events", usage.HistoryEntries), ("history", usage.HistoryBytes), ("receipts", usage.Receipts),
                ("saves", usage.Saves), ("losses", usage.Losses), ("recoveries", usage.Recoveries)) + "\n" +
                Text("persistentinv-storage-total", ("accounts", totals.Accounts), ("bytes", totals.PayloadBytes), ("over", totals.OverBudgetAccounts)),
            Message = message,
            History = Text("persistentinv-storage-history", ("pruned", usage.PrunedEvents)) + "\n\n" + DescribeHistory(history),
            Snapshots = revisions.Select(revision => new PersistentInventoryAdminSnapshot(revision.SnapshotId.Value,
                DescribeRevisionLabel(revision, header, lost))).ToArray(),
            CanSave = !busy && hasBody,
            CanInvalidate = !busy && header != null,
            CanQuarantine = !busy && header != null && !_entities.System<PersistentInventoryLifecycleSystem>().HasActiveBoundBody(user),
            CanRollback = canSelect,
            CanRecoverLost = canSelect && lost != null,
            CanViewLogs = _admins.GetAdminData(Player)?.HasFlag(AdminFlags.Logs) == true,
        };
        var snapshot = selected != null && revisions.Any(revision => revision.SnapshotId.Value == selected)
            ? selected : header?.CurrentVerified?.SnapshotId.Value ?? revisions.FirstOrDefault()?.SnapshotId.Value;
        if (snapshot != null)
            await InspectAsync(snapshot.Value);
    }

    private async Task InspectAsync(Guid snapshot)
    {
        if (_target is not { } user || snapshot == Guid.Empty)
            return;
        var stored = await _db.GetPersistentInventoryRevisionAsync(user, new PersistentInventorySnapshotId(snapshot));
        if (!await AuthorizeTargetAsync())
            return;
        _view.SelectedSnapshot = snapshot;
        _view.Items = [];
        if (stored == null)
        {
            _view.SnapshotDetails = Text("persistentinv-admin-snapshot-missing");
            return;
        }
        DescribeSnapshot(stored);
    }

    private async Task ActAsync(PersistentInventoryAdminRequest request)
    {
        if (!Enum.IsDefined(request.Action) || !await AuthorizeTargetAsync() || _target is not { } user)
            return;
        var header = await _db.GetPersistentInventoryHeaderAsync(user);
        if (!await AuthorizeTargetAsync())
            return;
        if ((header?.Revision.Value ?? 0) != request.Revision)
        {
            await ReloadAsync(request.Snapshot, Text("persistentinv-admin-stale"));
            return;
        }
        if (request.Action == PersistentInventoryAdminAction.OpenLogs)
        {
            if (_admins.GetAdminData(Player)?.HasFlag(AdminFlags.Logs) != true)
                return;
            var logs = new AdminLogsEui();
            Manager.OpenEui(logs, Player);
            logs.SetLogFilter(search: user.ToString(), types: [LogType.Mind]);
            return;
        }
        if (request.Action == PersistentInventoryAdminAction.Validate)
        {
            if (request.Snapshot == null)
                return;
            var stored = await _db.GetPersistentInventoryRevisionAsync(user, new(request.Snapshot.Value));
            if (!await AuthorizeTargetAsync() || stored == null)
                return;
            var serializer = _entities.System<PersistentInventorySnapshotSerializer>();
            var payload = PersistentInventoryPayloadCodec.Unpack(stored.Payload, stored.Metadata.PayloadSha256, serializer.GetConfiguredLimits());
            var restored = serializer.RestoreIsolated(payload);
            try
            {
                _view.Message = restored.Success ? Text("persistentinv-admin-validation-ok") :
                    Text("persistentinv-admin-validation-failed", ("error", restored.Error ?? ""));
            }
            finally { serializer.DeleteIsolated(restored); }
            return;
        }
        if (request.Action == PersistentInventoryAdminAction.ReloadCache)
        {
            await _inventory.LoadAsync(user);
            await ReloadAsync(request.Snapshot, Text("persistentinv-admin-cache-reloaded"));
            return;
        }
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 512)
        {
            _view.Message = Text("persistentinv-admin-reason-required");
            return;
        }
        if (TargetBusy(user))
        {
            await ReloadAsync(request.Snapshot, Text("persistentinv-admin-target-busy"));
            return;
        }
        var lifecycle = _entities.System<PersistentInventoryLifecycleSystem>();
        var actor = $"admin:{Player.UserId}";
        var actorId = Player.UserId.UserId;
        var reason = request.Reason.Trim();
        PersistentInventoryAdminMutationResult result;
        switch (request.Action)
        {
            case PersistentInventoryAdminAction.Save:
                if (!_players.TryGetSessionById(user, out var session) || session.AttachedEntity is not { } body)
                {
                    _view.Message = Text("persistentinv-admin-offline");
                    return;
                }
                var saved = await _entities.System<PersistentInventorySaveSystem>().TrySaveAsync(
                    new(user, body, session, PersistentInventorySaveSource.AdminCommand, null, actor, actorId, reason, request.Force));
                result = new(saved.IsSuccess, $"{saved.Status}: {saved.Message}");
                break;
            case PersistentInventoryAdminAction.Invalidate:
                result = await lifecycle.AdminInvalidateAsync(user, actor, actorId, reason);
                break;
            case PersistentInventoryAdminAction.Quarantine:
                result = await lifecycle.AdminQuarantineAsync(user, actor, actorId, reason);
                break;
            case PersistentInventoryAdminAction.Rollback when request.Snapshot is { } snapshot:
                result = await lifecycle.AdminRollbackAsync(user, new(snapshot), actor, actorId, reason);
                break;
            case PersistentInventoryAdminAction.RecoverLost:
                result = await lifecycle.AdminRecoverLostAsync(user, actor, actorId, reason);
                break;
            default:
                return;
        }
        _logs.Add(LogType.Mind, result.Success ? LogImpact.High : LogImpact.Medium,
            $"Persistent inventory panel {request.Action} for {_username} ({user}) by {Player.Name} ({Player.UserId}): " +
            $"success={result.Success}, snapshot={request.Snapshot}, forced={request.Force}. Reason: {reason}. Result: {result.Message}");
        if (await AuthorizeTargetAsync())
            await ReloadAsync(request.Snapshot, result.Message);
    }

    private string Text(string id, params (string, object)[] arguments)
        => _entities.System<WH40KPlayerCultureManager>().GetPlayerString(Player, id, arguments);
}
