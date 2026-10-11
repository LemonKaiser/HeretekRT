using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.Bank;
using Content.Server._WH40K.Quests;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Database;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared._WH40K.Merchant;
using Content.Shared._WH40K.Npcs;
using Robust.Server.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Merchant;

/// <summary>Private, revalidated quotes and a persistent account cooldown for native self-treatment.</summary>
public sealed partial class Wh40kMedicalTreatmentSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MerchantSystem _merchant = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private BloodstreamSystem _blood = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private Wh40kQuestSessionSystem _sessions = default!;

    private sealed record Quote(Guid Id, int Price, DamageSpecifier Damage, DateTime Expires, Guid Generation);
    private readonly Dictionary<(EntityUid Npc, NetUserId User), Quote> _quotes = new();
    private readonly Dictionary<(EntityUid Npc, NetUserId User), Guid> _loads = new();
    private readonly HashSet<NetUserId> _inFlight = new();
    private readonly Dictionary<NetUserId, (EntityUid Npc, Guid Operation, string Result)> _receipts = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kMedicalProviderComponent, BoundUIOpenedEvent>(Opened);
        SubscribeLocalEvent<Wh40kMedicalProviderComponent, BoundUIClosedEvent>(Closed);
        SubscribeLocalEvent<Wh40kMedicalProviderComponent, Wh40kMedicalRefresh>(Refresh);
        SubscribeLocalEvent<Wh40kMedicalProviderComponent, Wh40kMedicalConfirm>(Confirm);
        SubscribeLocalEvent<Wh40kMedicalProviderComponent, ComponentShutdown>(OnShutdown);
    }

    public bool Open(EntityUid body, EntityUid npc) => CanUse(npc, body, requireUi: false) &&
        _ui.TryOpenUi(npc, Wh40kMedicalUiKey.Key, body);

    private bool CanUse(EntityUid npc, EntityUid body, bool requireUi = true) =>
        !TerminatingOrDeleted(npc) && !TerminatingOrDeleted(body) && HasComp<Wh40kMedicalProviderComponent>(npc) &&
        HasComp<HumanoidAppearanceComponent>(body) && !HasComp<Wh40kNpcProtectionComponent>(body) &&
        _mobState.IsAlive(body) && _merchant.CanTrade(body) &&
        _interaction.InRangeUnobstructed(body, npc, 3, popup: false) &&
        (!requireUi || _ui.GetActors(npc, Wh40kMedicalUiKey.Key).Contains(body));

    private void Opened(Entity<Wh40kMedicalProviderComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey.Equals(Wh40kMedicalUiKey.Key)) RequestQuote(ent.Owner, args.Actor);
    }

    private void Closed(Entity<Wh40kMedicalProviderComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!args.UiKey.Equals(Wh40kMedicalUiKey.Key) || !TryComp(args.Actor, out ActorComponent? actor)) return;
        var key = (ent.Owner, actor.PlayerSession.UserId);
        _loads.Remove(key);
        _quotes.Remove(key);
    }

    private void OnShutdown(EntityUid uid, Wh40kMedicalProviderComponent comp, ComponentShutdown args)
    {
        foreach (var key in _loads.Keys.Where(key => key.Npc == uid).ToArray()) _loads.Remove(key);
        foreach (var key in _quotes.Keys.Where(key => key.Npc == uid).ToArray()) _quotes.Remove(key);
        foreach (var user in _receipts.Where(receipt => receipt.Value.Npc == uid).Select(receipt => receipt.Key).ToArray()) _receipts.Remove(user);
    }

    private void Refresh(EntityUid uid, Wh40kMedicalProviderComponent comp, Wh40kMedicalRefresh args)
        => RequestQuote(uid, args.Actor);

    private void RequestQuote(EntityUid npc, EntityUid body)
    {
        if (!CanUse(npc, body) || !TryComp(body, out ActorComponent? actor)) return;
        var user = actor.PlayerSession.UserId;
        if (_inFlight.Contains(user)) return;
        var request = Guid.NewGuid();
        _loads[(npc, user)] = request;
        _sessions.TrackWork(BuildQuote(npc, body, user, request));
    }

    private async Task BuildQuote(EntityUid npc, EntityUid body, NetUserId user, Guid request)
    {
        try
        {
            var states = await _db.GetWh40kQuestAccountStateAsync(user.UserId);
            await _sessions.OnSimulationThread(() =>
            {
                var key = (npc, user);
                if (_loads.GetValueOrDefault(key) != request || _inFlight.Contains(user) || !CanUse(npc, body) ||
                    !TryComp(body, out ActorComponent? actor) || actor.PlayerSession.UserId != user) return;
                _quotes.Remove(key);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var until = states.FirstOrDefault(state => state.Key == Wh40kMedicalTreatment.CooldownKey)?.Amount ?? 0;
                var remaining = (int) Math.Clamp(until - now, 0, int.MaxValue);
                var amount = TryComp(body, out DamageableComponent? damage) ? Math.Max(0, damage.TotalDamage.Float()) : 0;
                var config = Comp<Wh40kMedicalProviderComponent>(npc);
                var cost = Math.Clamp(config.BasePrice, 1, 100000) + Math.Ceiling(amount) * Math.Clamp(config.PricePerDamage, 1, 10000);
                var price = cost > int.MaxValue ? 0 : (int) cost;
                var status = remaining > 0 ? "wh40k-medical-cooldown" : amount <= 0 ? "wh40k-medical-healthy" :
                    price <= 0 ? "wh40k-medical-unavailable" : "wh40k-medical-ready";
                var id = Guid.Empty;
                if (status == "wh40k-medical-ready" && damage != null)
                {
                    id = Guid.NewGuid();
                    _quotes[key] = new(id, price, new DamageSpecifier(damage.Damage), DateTime.UtcNow.AddSeconds(30), _sessions.Generation);
                }
                _bank.TryGetBalance(body, out var balance);
                _ui.ServerSendUiMessage(npc, Wh40kMedicalUiKey.Key,
                    new Wh40kMedicalQuoteMessage(id, (int) Math.Min(Math.Ceiling(amount), int.MaxValue), amount > 0 ? price : 0,
                        balance, remaining, status), body);
            });
        }
        catch (Exception error)
        {
            Log.Error($"Medical quote failed: {error}");
            await _sessions.OnSimulationThread(() => Result(npc, body, Guid.Empty, "wh40k-medical-unavailable"));
        }
    }

    private void Confirm(EntityUid npc, Wh40kMedicalProviderComponent comp, Wh40kMedicalConfirm args)
    {
        var body = args.Actor;
        if (!CanUse(npc, body) || !TryComp(body, out ActorComponent? actor) || args.Operation == Guid.Empty) return;
        var user = actor.PlayerSession.UserId;
        if (_receipts.TryGetValue(user, out var receipt) && receipt.Npc == npc && receipt.Operation == args.Operation)
        { Result(npc, body, args.Operation, receipt.Result); return; }
        if (_inFlight.Contains(user)) return;
        if (!_quotes.TryGetValue((npc, user), out var quote) || quote.Id != args.Quote || !Matches(body, quote))
        { Result(npc, body, args.Operation, "wh40k-medical-changed"); RequestQuote(npc, body); return; }
        if (!_bank.CanBankWithdraw(body, quote.Price))
        { Result(npc, body, args.Operation, "wh40k-medical-no-money"); return; }
        _quotes.Remove((npc, user));
        _loads.Remove((npc, user));
        _inFlight.Add(user);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _sessions.TrackWork(Treat(npc, body, user, args.Operation, quote, now,
            now + Math.Clamp(comp.CooldownSeconds, 1, 86400)));
    }

    private bool Matches(EntityUid body, Quote quote) => quote.Expires > DateTime.UtcNow &&
        quote.Generation == _sessions.Generation && TryComp(body, out DamageableComponent? current) &&
        current.TotalDamage > 0 && current.Damage.DamageDict.Count == quote.Damage.DamageDict.Count &&
        current.Damage.DamageDict.All(entry => quote.Damage.DamageDict.TryGetValue(entry.Key, out var value) && value == entry.Value);

    private async Task Treat(EntityUid npc, EntityUid body, NetUserId user, Guid operation, Quote quote, long now, long until)
    {
        var reserved = false;
        var paid = false;
        var healed = false;
        var outcome = "wh40k-medical-unavailable";
        try
        {
            reserved = await _db.ReserveWh40kMedicalCooldownAsync(user.UserId, operation, now, until);
            await _sessions.OnSimulationThread(() =>
            {
                if (!reserved) { outcome = "wh40k-medical-cooldown"; return; }
                if (!CanUse(npc, body) || !TryComp(body, out ActorComponent? actor) ||
                    actor.PlayerSession.UserId != user || !Matches(body, quote))
                { outcome = "wh40k-medical-changed"; return; }
                if (!_bank.TryBankWithdraw(body, quote.Price)) { outcome = "wh40k-medical-no-money"; return; }
                paid = true;
                _damage.SetAllDamage(body, Comp<DamageableComponent>(body), 0);
                healed = true;
                if (TryComp(body, out BloodstreamComponent? blood))
                {
                    _blood.TryModifyBleedAmount(body, -blood.BleedAmount, blood);
                    _blood.TryModifyBloodLevel(body, blood.BloodMaxVolume, blood);
                }
                outcome = "wh40k-medical-success";
            });
        }
        catch (Exception error)
        {
            Log.Error($"Medical treatment failed: {error}");
            if (healed) outcome = "wh40k-medical-success";
        }
        finally
        {
            if (paid && !healed)
                await _sessions.OnSimulationThread(() =>
                {
                    if (!_bank.TryBankDeposit(body, quote.Price, tax: false)) Log.Error($"Medical refund failed for {user}, operation {operation}.");
                });
            if (reserved && !healed)
            {
                try { await _db.ReleaseWh40kMedicalCooldownAsync(user.UserId, operation); }
                catch (Exception error) { Log.Error($"Medical cooldown release failed: {error}"); }
            }
            await _sessions.OnSimulationThread(() =>
            {
                _inFlight.Remove(user);
                if (!TerminatingOrDeleted(npc)) _receipts[user] = (npc, operation, outcome);
                Result(npc, body, operation, outcome);
                RequestQuote(npc, body);
            });
        }
    }

    private void Result(EntityUid npc, EntityUid body, Guid operation, string outcome)
    {
        if (!TerminatingOrDeleted(npc) && !TerminatingOrDeleted(body) && _ui.GetActors(npc, Wh40kMedicalUiKey.Key).Contains(body))
            _ui.ServerSendUiMessage(npc, Wh40kMedicalUiKey.Key, new Wh40kMedicalResult(operation, outcome), body);
    }
}
