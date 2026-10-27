using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WH40K.Factions;
using Content.Shared.Preferences;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Network;

namespace Content.Server.Database;

public sealed record FactionBankMutation(string Faction, FactionBankEntry Entry, int Remainder = 0);
public sealed record FactionBankDelivery(int Slot, int Balance, Guid[] Entries);
public sealed record FactionBankData(FactionBankAccount[] Accounts, FactionBankTaxCounter[] Counters, FactionBankTransaction[] Entries);

public partial interface IServerDbManager
{
    Task<FactionBankData> GetFactionBankDataAsync();
    Task SaveFactionBankProfileAsync(NetUserId user, ICharacterProfile profile, int slot, FactionBankMutation mutation);
    Task QueueFactionBankPayoutAsync(FactionBankMutation mutation);
    Task<FactionBankDelivery?> DeliverFactionBankPayoutsAsync(NetUserId user);
    Task<FactionBankTransaction?> GetFactionBankEntryAsync(Guid id);
}

public sealed partial class ServerDbManager
{
    // Profile writes and treasury mutations must reach the database in cache update order.
    private readonly SemaphoreSlim _profileWrites = new(1, 1);

    private async Task<T> SerializeProfileWrite<T>(Func<Task<T>> write)
    {
        await _profileWrites.WaitAsync();
        try { return await write(); }
        finally { _profileWrites.Release(); }
    }

    private Task SerializeProfileWrite(Func<Task> write) => SerializeProfileWrite(async () => { await write(); return true; });

    public Task<FactionBankData> GetFactionBankDataAsync() => RunDbCommand(() => _db.GetFactionBankDataAsync());
    public Task<FactionBankTransaction?> GetFactionBankEntryAsync(Guid id) => RunDbCommand(() => _db.GetFactionBankEntryAsync(id));
    public Task SaveFactionBankProfileAsync(NetUserId user, ICharacterProfile profile, int slot, FactionBankMutation mutation)
        => SerializeProfileWrite(() => RunDbCommand(() => _db.SaveCharacterSlotAsync(user, profile, slot, mutation)));
    public Task QueueFactionBankPayoutAsync(FactionBankMutation mutation)
        => SerializeProfileWrite(() => RunDbCommand(() => _db.QueueFactionBankPayoutAsync(mutation)));
    public Task<FactionBankDelivery?> DeliverFactionBankPayoutsAsync(NetUserId user)
        => SerializeProfileWrite(() => RunDbCommand(() => _db.DeliverFactionBankPayoutsAsync(user)));
}

public abstract partial class ServerDbBase
{
    public async Task<FactionBankTransaction?> GetFactionBankEntryAsync(Guid id)
    {
        await using var db = await GetDb();
        return await db.DbContext.FactionBankTransactions.SingleOrDefaultAsync(e => e.Id == id);
    }

    public async Task<FactionBankData> GetFactionBankDataAsync()
    {
        await using var db = await GetDb();
        var accounts = await db.DbContext.FactionBankAccounts.ToArrayAsync();
        var counters = await db.DbContext.FactionBankTaxCounters.ToArrayAsync();
        var entries = new List<FactionBankTransaction>();
        foreach (var account in accounts)
            entries.AddRange(await db.DbContext.FactionBankTransactions.Where(e => e.CompanyId == account.CompanyId)
                .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Take(FactionBankRules.HistoryLimit).ToArrayAsync());
        return new FactionBankData(accounts, counters, entries.ToArray());
    }

    private static async Task ApplyFactionBankMutation(ServerDbContext db, FactionBankMutation mutation)
    {
        var entry = mutation.Entry;
        if (entry.Gross <= 0 || entry.Amount < 0 || entry.Kind == FactionBankEntryKind.Payout && entry.Amount <= 0 ||
            mutation.Remainder is < 0 or >= 100)
            throw new InvalidOperationException("Invalid faction bank operation.");
        if (await db.FactionBankTransactions.AnyAsync(e => e.Id == entry.Id))
            throw new InvalidOperationException("Duplicate faction bank operation.");
        var account = await db.FactionBankAccounts.AsTracking().SingleOrDefaultAsync(a => a.CompanyId == mutation.Faction);
        if (account == null)
        {
            account = new FactionBankAccount { CompanyId = mutation.Faction };
            db.FactionBankAccounts.Add(account);
        }
        if (entry.Kind == FactionBankEntryKind.Payout)
        {
            if (account.Balance < entry.Amount)
                throw new InvalidOperationException("Insufficient faction funds.");
            account.Balance -= entry.Amount;
        }
        else
        {
            account.Balance = checked(account.Balance + entry.Amount);
            var counter = await db.FactionBankTaxCounters.AsTracking().SingleOrDefaultAsync(c =>
                c.CompanyId == mutation.Faction && c.UserId == entry.Actor);
            if (counter == null)
            {
                counter = new FactionBankTaxCounter { CompanyId = mutation.Faction, UserId = entry.Actor };
                db.FactionBankTaxCounters.Add(counter);
            }
            counter.Remainder = mutation.Remainder;
        }
        db.FactionBankTransactions.Add(new FactionBankTransaction
        {
            Id = entry.Id, CompanyId = mutation.Faction, Kind = (byte) entry.Kind,
            Actor = entry.Actor, Recipient = entry.Recipient, Name = entry.Name, Gross = entry.Gross,
            Amount = entry.Amount, Memo = entry.Memo, CreatedAt = entry.CreatedAt, Pending = entry.Pending,
        });
    }

    public async Task QueueFactionBankPayoutAsync(FactionBankMutation mutation)
    {
        await using var db = await GetDb();
        await ApplyFactionBankMutation(db.DbContext, mutation);
        await db.DbContext.SaveChangesAsync();
    }

    public async Task<FactionBankDelivery?> DeliverFactionBankPayoutsAsync(NetUserId user)
    {
        await using var db = await GetDb();
        var pending = await db.DbContext.FactionBankTransactions.AsTracking().Where(e =>
            e.Recipient == user.UserId && e.Pending && e.Kind == (byte) FactionBankEntryKind.Payout)
            .OrderBy(e => e.CreatedAt).ToListAsync();
        if (pending.Count == 0)
            return null;
        var profile = await db.DbContext.Profile.AsTracking().Include(p => p.Preference)
            .SingleOrDefaultAsync(p => p.Preference.UserId == user.UserId && p.Slot == p.Preference.SelectedCharacterSlot);
        if (profile == null)
            return null;
        var delivered = false;
        foreach (var entry in pending)
        {
            if ((long) profile.BankBalance + entry.Amount > int.MaxValue)
                continue;
            profile.BankBalance += entry.Amount;
            entry.Pending = false;
            delivered = true;
        }
        if (!delivered)
            return null;
        await db.DbContext.SaveChangesAsync();
        return new FactionBankDelivery(profile.Slot, profile.BankBalance, pending.Where(e => !e.Pending).Select(e => e.Id).ToArray());
    }
}
