using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared.CCVar;
using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.PersistentInventory;

public sealed partial class PersistentInventoryStorageSystem : EntitySystem
{
    private static readonly Gauge StoredBytes = Metrics.CreateGauge("wh40k_persistent_inventory_stored_payload_bytes", "Inventory payload bytes, excluding database overhead.");
    private static readonly Gauge Accounts = Metrics.CreateGauge("wh40k_persistent_inventory_stored_accounts", "Accounts which have used persistent inventory.");
    private static readonly Gauge History = Metrics.CreateGauge("wh40k_persistent_inventory_history_entries", "Retained persistent inventory history entries.");
    private static readonly Gauge OverBudget = Metrics.CreateGauge("wh40k_persistent_inventory_legacy_over_budget_accounts", "Legacy accounts exceeding the new storage limits.");

    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly CancellationTokenSource _shutdown = new();
    private Task _maintenance = Task.CompletedTask;
    private TimeSpan _nextPass;
    private Guid? _nextUser;

    public override void Update(float frameTime)
    {
        if (!_configuration.GetCVar(CCVars.Wh40kPersistentInventoryEnabled) || !_maintenance.IsCompleted || _timing.CurTime < _nextPass)
            return;
        _nextPass = _timing.CurTime + TimeSpan.FromMinutes(1);
        _maintenance = MaintainAsync(_shutdown.Token);
    }

    private async Task MaintainAsync(CancellationToken cancel)
    {
        try
        {
            var result = await _db.MaintainPersistentInventoryStorageAsync(_nextUser, cancel: cancel);
            _nextUser = result.NextUser;
            var totals = await _db.GetPersistentInventoryStorageTotalsAsync(cancel);
            StoredBytes.Set(totals.PayloadBytes);
            Accounts.Set(totals.Accounts);
            History.Set(totals.HistoryEntries);
            OverBudget.Set(totals.OverBudgetAccounts);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error) { Log.Error($"Persistent inventory maintenance failed: {error}"); }
    }

    public override void Shutdown()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        base.Shutdown();
    }
}
