using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WH40K.Progression;
using Content.Shared._WH40K.Quests;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Network;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    public async Task<Wh40kMerchantSale?> GetWh40kMerchantSaleAsync(Guid token, CancellationToken cancel = default)
    {
        await using var guard = await GetDb(cancel);
        return await guard.DbContext.Wh40kMerchantSales.AsNoTracking().SingleOrDefaultAsync(x => x.Token == token, cancel);
    }

    public Task<bool> ReserveWh40kMerchantSaleAsync(Wh40kMerchantSale sale, CancellationToken cancel = default)
    {
        if (sale.Token == Guid.Empty || sale.UserId == Guid.Empty || sale.Operation == Guid.Empty || sale.Price is <= 0 or > 1000000 ||
            sale.State != 0 || sale.Merchant.Length is < 1 or > 128 || sale.Product.Length is < 1 or > 128 ||
            sale.PayloadJson.Length is < 1 or > 131072 || sale.CreatedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Invalid merchant sale.");
        return QuestWriteAsync(new(Wh40kQuestOwnerScope.Account, sale.UserId), async db =>
        {
            var prior = await db.Wh40kMerchantSales.FindAsync([sale.Token], cancel);
            if (prior != null && prior.State != 2)
                return prior.Operation == sale.Operation && prior.UserId == sale.UserId && prior.Merchant == sale.Merchant &&
                    prior.Product == sale.Product && prior.Price == sale.Price && prior.PayloadJson == sale.PayloadJson && prior.State <= 1;
            if (await db.Wh40kMerchantSales.AnyAsync(x => x.Operation == sale.Operation, cancel)) return false;
            if (prior == null) db.Wh40kMerchantSales.Add(sale);
            else db.Entry(prior).CurrentValues.SetValues(sale);
            return true;
        }, cancel);
    }

    public Task<bool> FinishWh40kMerchantSaleAsync(Guid user, Guid token, Guid operation, bool pay, CancellationToken cancel = default)
        => QuestWriteAsync(new(Wh40kQuestOwnerScope.Account, user), async db =>
        {
            var row = await db.Wh40kMerchantSales.FindAsync([token], cancel);
            if (row == null || row.UserId != user || row.Operation != operation) return false;
            if (row.State != 0) return pay ? row.State == 1 : row.State == 2;
            row.State = pay ? 1 : 2;
            if (pay)
            {
                // Commercial payout uses the established fenced material outbox, never a quest reward intent/XP ledger.
                AddWh40kRewardDeliveries(db, new NetUserId(user), [new Wh40kRewardDeliveryDraft(
                    "merchant:" + operation.ToString("N"), "payment", "currency", null, row.Price, null)], DateTime.UtcNow);
            }
            return true;
        }, cancel);
}
