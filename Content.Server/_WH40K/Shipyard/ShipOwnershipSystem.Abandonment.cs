using System.Threading;
using System.Threading.Tasks;
using Content.Server._NF.Bank;
using Content.Server._NF.CryoSleep;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.Shipyard;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Robust.Shared.Asynchronous;
using Robust.Shared.Network;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipOwnershipSystem
{
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IServerDbManager _database = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private ShipyardPassengerSystem _passengers = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private WH40KPlayerCultureManager _culture = default!;

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);
    private TimeSpan _nextCleanup;
    private readonly HashSet<EntityUid> _automaticSales = new();

    private void InitializeAbandonment()
    {
        SubscribeLocalEvent<CryosleepEnterEvent>(OnCryosleep);
        SubscribeLocalEvent<CryosleepWakeUpEvent>(OnWakeUp);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _automaticSales.Clear();
            _nextCleanup = TimeSpan.Zero;
        });
    }

    private void OnCryosleep(CryosleepEnterEvent args)
    {
        if (args.User is not { } user)
            return;

        var departed = false;
        var ships = EntityQueryEnumerator<ShipOwnershipComponent>();
        while (ships.MoveNext(out var uid, out var ownership))
        {
            if (ownership.OwnerUserId != user || ownership.InCryosleep)
                continue;

            ownership.InCryosleep = true;
            if (ownership.IsOwnerOnline)
                ownership.LastStatusChangeTime = _gameTiming.CurTime;
            ownership.IsOwnerOnline = false;
            Dirty(uid, ownership);
            departed = true;
        }

        if (departed && _playerManager.TryGetSessionById(user, out var session))
            _chat.DispatchServerMessage(session, _culture.GetPlayerString(session, "wh40k-ship-cleanup-cryo"));

        // Process after the cryo operation finishes retiring the body and its inventory.
        _nextCleanup = TimeSpan.Zero;
    }

    private void OnWakeUp(CryosleepWakeUpEvent args)
    {
        if (args.User is not { } user)
            return;

        var ships = EntityQueryEnumerator<ShipOwnershipComponent>();
        while (ships.MoveNext(out var uid, out var ownership))
        {
            if (ownership.OwnerUserId != user)
                continue;

            ownership.InCryosleep = false;
            ownership.IsOwnerOnline = true;
            ownership.LastStatusChangeTime = _gameTiming.CurTime;
            Dirty(uid, ownership);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_gameTiming.CurTime < _nextCleanup)
            return;

        _nextCleanup = _gameTiming.CurTime + CleanupInterval;
        var candidates = new List<Entity<ShipOwnershipComponent>>();
        var ships = EntityQueryEnumerator<ShipOwnershipComponent>();
        while (ships.MoveNext(out var uid, out var ownership))
        {
            if (!ownership.IsOwnerOnline)
                candidates.Add((uid, ownership));
        }

        foreach (var ship in candidates)
            TryCleanupAbandonedShip(ship);
    }

    /// <summary>
    /// Retires an absent owner's ship; occupied ships away from a station are retried later.
    /// </summary>
    public bool TryCleanupAbandonedShip(Entity<ShipOwnershipComponent> ship)
    {
        if (TerminatingOrDeleted(ship) || !TryComp<ShipOwnershipComponent>(ship, out var current) ||
            EntityManager.System<Content.Server._WH40K.ShuttleStorage.ShuttleStorageSystem>().IsTransferring(ship) ||
            !ReferenceEquals(current, ship.Comp) || ship.Comp.IsOwnerOnline || ship.Comp.OwnerUserId == default ||
            !HasComp<ShuttleComponent>(ship) || _automaticSales.Contains(ship))
            return false;

        var expired = (_gameTiming.CurTime - ship.Comp.LastStatusChangeTime).TotalSeconds >=
                      Math.Max(0, ship.Comp.DeletionTimeoutSeconds);
        if (_shipyard.TryGetStationBerth(ship, out _, out _))
        {
            if (!ship.Comp.InCryosleep && !expired)
                return false;

            _automaticSales.Add(ship);
            _ = PrepareAutomaticSaleAsync(ship);
            return true;
        }

        if (!expired || _passengers.HasPassengers(ship))
            return false;

        return _shipyard.TryDeleteAbandonedShuttle(ship);
    }

    private async Task PrepareAutomaticSaleAsync(Entity<ShipOwnershipComponent> ship)
    {
        var owner = ship.Comp.OwnerUserId;
        try
        {
            var preferences = _preferences.TryGetCachedPreferences(owner, out var cached)
                ? cached
                : await _database.GetPlayerPreferencesAsync(owner, CancellationToken.None);

            _tasks.RunOnMainThread(() => CompleteAutomaticSale(ship, owner, preferences));
        }
        catch (Exception exception)
        {
            _tasks.RunOnMainThread(() => _automaticSales.Remove(ship));
            Log.Error($"Unable to prepare automatic sale of {ship.Owner}: {exception}");
        }
    }

    private void CompleteAutomaticSale(Entity<ShipOwnershipComponent> ship, NetUserId owner, PlayerPreferences? preferences)
    {
        try
        {
            if (TerminatingOrDeleted(ship) || !TryComp<ShipOwnershipComponent>(ship, out var current) ||
                !ReferenceEquals(current, ship.Comp) || current.OwnerUserId != owner || current.IsOwnerOnline ||
                !current.InCryosleep && (_gameTiming.CurTime - current.LastStatusChangeTime).TotalSeconds <
                Math.Max(0, current.DeletionTimeoutSeconds))
                return;

            // A connected owner may have changed character while an offline load was pending.
            if (_preferences.TryGetCachedPreferences(current.OwnerUserId, out var cached))
                preferences = cached;
            var slot = current.OwnerCharacterSlot ?? preferences?.SelectedCharacterIndex;
            var profile = slot is { } index && preferences?.Characters.TryGetValue(index, out var character) == true
                ? character as HumanoidCharacterProfile
                : null;
            var voucher = TryComp<ShuttleDeedComponent>(ship, out var deed) && deed.PurchasedWithVoucher;
            if (!voucher && (preferences == null || profile == null))
            {
                Log.Warning($"Automatic sale of {ToPrettyString(ship)} deferred: the owner's character is unavailable.");
                return;
            }

            if (!_shipyard.TryAutomaticSale(ship,
                    amount => TryCreditAutomaticSale(current.OwnerUserId, preferences!, profile!, amount), out var proceeds))
                return;

            Log.Info($"Automatically sold {ToPrettyString(ship)} for {proceeds} after departure of {current.OwnerUserId}.");
            if (_playerManager.TryGetSessionById(current.OwnerUserId, out var session))
                _chat.DispatchServerMessage(session, _culture.GetPlayerString(session, "wh40k-ship-cleanup-sold",
                    ("ship", Name(ship)), ("amount", proceeds)));
        }
        finally
        {
            _automaticSales.Remove(ship);
        }
    }

    private bool TryCreditAutomaticSale(NetUserId owner, PlayerPreferences preferences,
        HumanoidCharacterProfile profile, int proceeds)
    {
        if (_playerManager.TryGetSessionById(owner, out var session))
            return _bank.TryBankDeposit(session, preferences, profile, proceeds, out _);

        if (!_bank.TryBeginBankDepositOffline(owner, preferences, profile, proceeds, out var credit))
            return false;

        _ = ObserveCreditAsync();
        return true;

        async Task ObserveCreditAsync()
        {
            try
            {
                await credit;
            }
            catch (Exception exception)
            {
                Log.Error($"Automatic shuttle sale credit of {proceeds} to {owner} failed: {exception}");
            }
        }
    }
}
