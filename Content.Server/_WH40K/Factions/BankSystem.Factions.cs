using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Mono.Company;
using Content.Server._WH40K.Administration;
using Content.Server.Database;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Bank.Events;
using Content.Shared._WH40K.Factions;
using Content.Shared._WH40K.Progression;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._NF.Bank;

public sealed partial class BankSystem
{
    [Dependency] private CompanyManager _factions = default!;
    private readonly Dictionary<string, long> _factionBalances = new();
    private readonly Dictionary<(string, Guid), int> _factionTaxRemainders = new();
    private readonly Dictionary<string, List<FactionBankEntry>> _factionHistory = new();
    private readonly HashSet<Task> _factionIncomeWrites = new();
    private readonly HashSet<NetUserId> _factionPayoutBusy = new();
    private readonly HashSet<Guid> _deliveredFactionPayouts = new();
    private bool _factionBankHealthy;
    public Task FactionBankReady { get; private set; } = Task.CompletedTask;
    public event Action<string>? FactionBankChanged;

    private async Task LoadFactionBankAsync()
    {
        try
        {
            var data = await _db.GetFactionBankDataAsync();
            foreach (var account in data.Accounts)
                _factionBalances[account.CompanyId] = account.Balance;
            foreach (var counter in data.Counters)
                _factionTaxRemainders[(counter.CompanyId, counter.UserId)] = counter.Remainder;
            foreach (var row in data.Entries.OrderBy(e => e.CreatedAt))
                AddFactionHistory(row.CompanyId, new FactionBankEntry(row.Id, (FactionBankEntryKind) row.Kind,
                    row.Actor, row.Recipient, row.Name, row.Gross, row.Amount, row.Memo, row.CreatedAt, row.Pending));
            _factionBankHealthy = true;
        }
        catch (Exception e) { _log.Error($"Unable to load faction banking: {e}"); }
    }

    public FactionBankSnapshot? GetFactionBankSnapshot(NetUserId viewer, string faction)
    {
        if (!FactionBankRules.CanManage(_factions.GetRole(viewer, faction)))
            return null;
        return new FactionBankSnapshot(_factionBalances.GetValueOrDefault(faction),
            _factionHistory.GetValueOrDefault(faction)?.OrderByDescending(e => e.CreatedAt).Take(FactionBankRules.HistoryLimit).ToArray() ?? [],
            _factionBankHealthy && FactionBankReady.IsCompletedSuccessfully);
    }

    public int GetFactionIncomeTax(NetUserId user, int gross, out string? faction)
    {
        faction = _factions.GetActiveFaction(user)?.Id;
        if (gross <= 0 || faction == null)
            return 0;
        return FactionBankRules.Tax(gross, _factionTaxRemainders.GetValueOrDefault((faction, user.UserId))).Tax;
    }

    private bool PrepareFactionIncome(NetUserId user, string name, int balance, int gross, bool taxable,
        out int net, out FactionBankMutation? mutation)
    {
        net = gross;
        mutation = null;
        if (_factionPayoutBusy.Contains(user) || EntityManager.System<Wh40kPlayerResetSystem>().IsResetting(user))
            return false;
        var faction = taxable ? _factions.GetActiveFaction(user)?.Id : null;
        if (faction == null)
            return (long) balance + gross <= int.MaxValue;
        if (!_factionBankHealthy || !FactionBankReady.IsCompletedSuccessfully)
            return false;
        var split = FactionBankRules.Tax(gross, _factionTaxRemainders.GetValueOrDefault((faction, user.UserId)));
        if ((long) balance + split.Net > int.MaxValue || _factionBalances.GetValueOrDefault(faction) > long.MaxValue - split.Tax)
            return false;
        net = split.Net;
        mutation = new FactionBankMutation(faction, new FactionBankEntry(Guid.NewGuid(), FactionBankEntryKind.Income,
            user.UserId, user.UserId, name, gross, split.Tax, "", DateTime.UtcNow), split.Remainder);
        _factionBalances[faction] = _factionBalances.GetValueOrDefault(faction) + split.Tax;
        _factionTaxRemainders[(faction, user.UserId)] = split.Remainder;
        return true;
    }

    private void TrackFactionIncome(FactionBankMutation mutation, Task write)
    {
        _factionIncomeWrites.Add(write);
        _ = CompleteAsync();
        async Task CompleteAsync()
        {
            try
            {
                await write;
                AddFactionHistory(mutation.Faction, mutation.Entry);
                FactionBankChanged?.Invoke(mutation.Faction);
            }
            catch (Exception e)
            {
                _factionBankHealthy = false;
                _log.Error($"Faction income {mutation.Entry.Id} could not be saved; treasury transfers are disabled: {e}");
            }
            finally { _factionIncomeWrites.Remove(write); }
        }
    }

    public void NotifyFactionPayoutDelivery(Guid[] entries)
    {
        _deliveredFactionPayouts.UnionWith(entries);
        foreach (var (faction, history) in _factionHistory)
        {
            for (var i = 0; i < history.Count; i++)
            {
                if (_deliveredFactionPayouts.Contains(history[i].Id))
                    history[i] = history[i] with { Pending = false };
            }
            FactionBankChanged?.Invoke(faction);
        }
    }

    private void AddFactionHistory(string faction, FactionBankEntry entry)
    {
        if (!_factionHistory.TryGetValue(faction, out var history))
            _factionHistory[faction] = history = new List<FactionBankEntry>();
        history.Add(_deliveredFactionPayouts.Contains(entry.Id) ? entry with { Pending = false } : entry);
        if (history.Count > FactionBankRules.HistoryLimit)
            history.RemoveAt(0);
    }

    /// <summary>Caller holds CompanyManager.FactionOperations across authorization and commit.</summary>
    public async Task<Wh40kFactionUiOperationStatus> PayFactionAsync(NetUserId actor, string faction, NetUserId recipient, FactionBankPayout? payout)
    {
        await FactionBankReady;
        if (!FactionBankRules.CanManage(_factions.GetRole(actor, faction)) || !_factions.IsMember(recipient, faction))
            return Wh40kFactionUiOperationStatus.NotAuthorized;
        if (payout == null || payout.RequestId == Guid.Empty || payout.Amount <= 0 || payout.Memo == null || payout.Memo.Length > FactionBankRules.MaxMemoLength ||
            payout.Memo.Any(char.IsControl))
            return Wh40kFactionUiOperationStatus.InvalidPayout;
        if (!_factionBankHealthy)
            return Wh40kFactionUiOperationStatus.BankUnavailable;
        if (await _db.GetFactionBankEntryAsync(payout.RequestId) is { } previous)
        {
            if (previous.CompanyId != faction || previous.Actor != actor.UserId || previous.Recipient != recipient.UserId ||
                previous.Kind != (byte) FactionBankEntryKind.Payout || previous.Amount != payout.Amount || previous.Memo != payout.Memo.Trim())
                return Wh40kFactionUiOperationStatus.InvalidPayout;
            return previous.Pending ? Wh40kFactionUiOperationStatus.PayoutQueued : Wh40kFactionUiOperationStatus.PayoutSent;
        }
        if (_factionIncomeWrites.Count > 0)
            await Task.WhenAll(_factionIncomeWrites.ToArray());
        if (_factionBalances.GetValueOrDefault(faction) < payout.Amount)
            return Wh40kFactionUiOperationStatus.InsufficientFactionFunds;
        if (!_factionPayoutBusy.Add(recipient))
            return Wh40kFactionUiOperationStatus.BankUnavailable;

        var reserved = false;
        var credited = false;
        var slot = -1;
        try
        {
            var offlinePrefs = _prefsManager.TryGetCachedPreferences(recipient, out var cached) ? null
                : await _db.GetPlayerPreferencesAsync(recipient, CancellationToken.None);
            var online = _playerManager.TryGetSessionById(recipient, out var session);
            var prefs = _prefsManager.TryGetCachedPreferences(recipient, out cached) ? cached : offlinePrefs;
            if (prefs?.SelectedCharacter is not HumanoidCharacterProfile profile ||
                EntityManager.System<Wh40kPlayerResetSystem>().IsResetting(recipient) ||
                online && (!session!.Channel.AuthType.HasStaticUserId() || cached == null))
                return Wh40kFactionUiOperationStatus.RecipientUnavailable;
            if ((long) profile.BankBalance + payout.Amount > int.MaxValue)
                return Wh40kFactionUiOperationStatus.RecipientBalanceLimit;
            if (!FactionBankRules.CanManage(_factions.GetRole(actor, faction)) || !_factions.IsMember(recipient, faction))
                return Wh40kFactionUiOperationStatus.NotAuthorized;

            var entry = new FactionBankEntry(payout.RequestId, FactionBankEntryKind.Payout, actor.UserId, recipient.UserId,
                _factions.GetCompanyMember(faction, recipient)?.LastSeenUserName ?? recipient.ToString(),
                payout.Amount, payout.Amount, payout.Memo.Trim(), DateTime.UtcNow, !online);
            var mutation = new FactionBankMutation(faction, entry);
            _factionBalances[faction] -= payout.Amount;
            reserved = true;
            if (online)
            {
                slot = prefs.IndexOfCharacter(profile);
                var write = _prefsManager.SetFactionBankProfileAsync(recipient, slot, profile.WithBankBalance(profile.BankBalance + payout.Amount), mutation);
                credited = _prefsManager.TryGetCachedPreferences(recipient, out var updated) &&
                    updated.Characters.TryGetValue(slot, out var changed) && changed is HumanoidCharacterProfile changedProfile &&
                    changedProfile.BankBalance == profile.BankBalance + payout.Amount;
                await write;
                SynchronizeFactionRecipient(session!);
            }
            else
                await _db.QueueFactionBankPayoutAsync(mutation);
            AddFactionHistory(faction, entry);
            FactionBankChanged?.Invoke(faction);
            return online ? Wh40kFactionUiOperationStatus.PayoutSent : Wh40kFactionUiOperationStatus.PayoutQueued;
        }
        catch (Exception e)
        {
            if (reserved)
                _factionBalances[faction] += payout.Amount;
            if (credited && _prefsManager.TryGetCachedPreferences(recipient, out var prefs) &&
                prefs.Characters.TryGetValue(slot, out var current) && current is HumanoidCharacterProfile humanoid)
            {
                try { await _prefsManager.SetProfile(recipient, slot, humanoid.WithBankBalance(Math.Max(0, humanoid.BankBalance - payout.Amount))); }
                catch (Exception rollback) { _log.Error($"Faction payout rollback failed: {rollback}"); }
            }
            _log.Error($"Faction payout to {recipient} failed: {e}");
            return Wh40kFactionUiOperationStatus.Failed;
        }
        finally { _factionPayoutBusy.Remove(recipient); }
    }

    private void SynchronizeFactionRecipient(ICommonSession session)
    {
        if (!_prefsManager.TryGetCachedPreferences(session.UserId, out var prefs) || prefs.SelectedCharacter is not HumanoidCharacterProfile profile)
            return;
        if (session.AttachedEntity is { } body && TryComp<BankAccountComponent>(body, out var bank))
            UpdateBankBalance(body, bank);
        RaiseLocalEvent(new BalanceChangedEvent(session, profile.BankBalance));
    }
}
