using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._WH40K.Economy;

namespace Content.Server._WH40K.Economy;

public sealed partial class MaterialMarketSystem
{
    [Dependency] private IServerDbManager _database = default!;
    private readonly Dictionary<Guid, PendingSale> _pending = new();
    private readonly HashSet<Guid> _livePending = new();
    private readonly Dictionary<Guid, Task<Wh40kMerchantSale?>> _recoveries = new();
    private TimeSpan _nextRecovery;

    public bool TryReserve(MaterialMarketQuote quote, Guid operation, Guid itemToken)
    {
        if (operation == Guid.Empty || itemToken == Guid.Empty || _pending.ContainsKey(operation) || !IsCurrent(quote))
            return false;
        _pending[operation] = new PendingSale(operation, itemToken,
            SaturationEnabled ? new(quote.Materials) : new(), DateTime.UtcNow);
        _livePending.Add(operation);
        if (!SaveState())
        {
            _pending.Remove(operation);
            _livePending.Remove(operation);
            return false;
        }
        _revision++;
        return true;
    }

    public void FinishReservation(Guid operation, bool? paid)
    {
        _livePending.Remove(operation);
        if (paid == null || !_pending.TryGetValue(operation, out var pending))
            return;
        var previous = new Dictionary<string, MarketLoad>(_loads);
        _pending.Remove(operation);
        if (paid.Value)
        {
            foreach (var (id, quantity) in pending.Materials)
            {
                var recovered = MaterialMarketMath.Recover(quantity, Math.Max(0, (DateTime.UtcNow - pending.AtUtc).TotalSeconds),
                    Profile.RecoveryPerSecond);
                _loads[id] = new MarketLoad(GetCommittedLoad(id) + recovered, _timing.CurTime);
            }
        }
        if (!SaveState())
        {
            _pending[operation] = pending;
            _loads.Clear();
            foreach (var (key, load) in previous)
                _loads[key] = load;
        }
        _revision++;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!Enabled || _timing.CurTime < _nextRecovery)
            return;
        _nextRecovery = _timing.CurTime + TimeSpan.FromSeconds(10);
        LoadState();
        EnsureDemandCycle();
        foreach (var (operation, pending) in _pending.ToArray())
        {
            if (_livePending.Contains(operation))
                continue;
            if (!_recoveries.TryGetValue(operation, out var task))
            {
                _recoveries[operation] = _database.GetWh40kMerchantSaleAsync(pending.ItemToken);
                continue;
            }
            if (!task.IsCompleted)
                continue;
            _recoveries.Remove(operation);
            if (!task.IsCompletedSuccessfully)
            {
                Log.Error($"Material market sale {operation} awaits database recovery: {task.Exception}");
                continue;
            }
            var row = task.GetAwaiter().GetResult();
            if (row == null || row.Operation != operation || row.State == 2)
                FinishReservation(operation, false);
            else if (row.State == 1)
                FinishReservation(operation, true);
        }
    }

    public sealed record PendingSale(Guid Operation, Guid ItemToken, Dictionary<string, double> Materials, DateTime AtUtc);
}
