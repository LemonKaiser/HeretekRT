using System.Threading;
using System.Threading.Tasks;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<Wh40kMerchantSale?> GetWh40kMerchantSaleAsync(Guid token, CancellationToken cancel = default);
    Task<bool> ReserveWh40kMerchantSaleAsync(Wh40kMerchantSale sale, CancellationToken cancel = default);
    Task<bool> FinishWh40kMerchantSaleAsync(Guid user, Guid token, Guid operation, bool pay, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<Wh40kMerchantSale?> GetWh40kMerchantSaleAsync(Guid token, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetWh40kMerchantSaleAsync(token, cancel));
    public Task<bool> ReserveWh40kMerchantSaleAsync(Wh40kMerchantSale sale, CancellationToken cancel = default)
        => RunDbCommand(() => _db.ReserveWh40kMerchantSaleAsync(sale, cancel));
    public Task<bool> FinishWh40kMerchantSaleAsync(Guid user, Guid token, Guid operation, bool pay, CancellationToken cancel = default)
        => RunDbCommand(() => _db.FinishWh40kMerchantSaleAsync(user, token, operation, pay, cancel));
}
