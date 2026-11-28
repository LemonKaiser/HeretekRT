using Content.Server.Radio.EntitySystems;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>Raised before a cargo pallet sale pays out so seller heat can trim narcotic payouts and tip off security.</summary>
[ByRefEvent]
public record struct Wh40kPalletSaleEvent(
    EntityUid Console,
    EntityUid Grid,
    EntityUid? Seller,
    HashSet<EntityUid> Goods,
    double Modifier)
{
    public double Cut;
}

/// <summary>Raised when a cargo pallet console appraises goods so the displayed price reflects seller heat.</summary>
[ByRefEvent]
public record struct Wh40kPalletAppraisalEvent(
    EntityUid Console,
    EntityUid Grid,
    EntityUid Viewer,
    HashSet<EntityUid> Goods,
    double Modifier)
{
    public double Cut;
}

/// <summary>
/// Heat on narcotics sellers. Each seller accumulates heat from narcotics sold at cargo pallets, which cools over time.
/// Hot sellers earn a smaller share of street value and generate progressively more detailed tips on security radio.
/// </summary>
public sealed class Wh40kNarcoticHeatSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private Wh40kNarcoticGoodsSystem _goods = default!;

    public static readonly ProtoId<RadioChannelPrototype>[] TipChannels = { "Security", "Nfsd", "Freelance" };
    public static readonly LocId Narcotics = "wh40k-narcotics-tip-goods";

    private const double Near = 0.8;
    private static readonly string[] WornSlots = { "outerClothing", "jumpsuit" };

    private readonly Dictionary<SellerKey, Seller> _sellers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kPalletSaleEvent>(OnSale);
        SubscribeLocalEvent<Wh40kPalletAppraisalEvent>(OnAppraisal);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _sellers.Clear());
    }

    private void OnSale(ref Wh40kPalletSaleEvent args)
    {
        if (args.Seller is not { } seller)
            return;

        var value = Value(args.Goods);
        if (value <= 0)
            return;

        args.Cut += (value - Payout(seller, value)) * args.Modifier;
        Sold(seller, args.Console, args.Grid, value);
    }

    public float Share(EntityUid seller, double value)
    {
        return value <= 0 ? 1f : (float) (Payout(seller, value) / value);
    }

    public Wh40kNarcoticHeatTier Sold(EntityUid seller, EntityUid buyer, EntityUid grid, double value, LocId? goods = null)
    {
        var record = _sellers.GetOrNew(Key(seller));
        var heat = Cooled(record);
        var free = Free(record);
        record.Heat = Math.Min(heat + value, _cfg.GetCVar(Wh40kNarcoticsCVars.HeatCap));
        record.Updated = _timing.CurTime;
        record.LastSale = record.Updated;

        var tier = Wh40kNarcoticHeatMath.Tier(record.Heat, free, _cfg.GetCVar(Wh40kNarcoticsCVars.HeatHot));
        _popup.PopupEntity(Loc.GetString(tier switch
        {
            Wh40kNarcoticHeatTier.Hot => "wh40k-narcotics-buyer-sold-hot",
            Wh40kNarcoticHeatTier.Warm => "wh40k-narcotics-buyer-sold-warm",
            _ when record.Heat > free * Near => "wh40k-narcotics-buyer-sold-near",
            _ => "wh40k-narcotics-buyer-sold-cold",
        }), buyer, seller, PopupType.Medium);

        Tip(seller, buyer, grid, record, tier, goods);
        return tier;
    }

    private void OnAppraisal(ref Wh40kPalletAppraisalEvent args)
    {
        var value = Value(args.Goods);
        if (value <= 0)
            return;

        var record = _sellers.GetOrNew(Key(args.Viewer));
        var heat = Cooled(record);
        args.Cut += (value - Payout(record, heat, value)) * args.Modifier;

        _popup.PopupEntity(Loc.GetString(Tier(record, heat + value) switch
        {
            Wh40kNarcoticHeatTier.Hot => "wh40k-narcotics-buyer-appraise-hot",
            Wh40kNarcoticHeatTier.Warm => "wh40k-narcotics-buyer-appraise-warm",
            _ when heat + value > Free(record) * Near => "wh40k-narcotics-buyer-appraise-near",
            _ => "wh40k-narcotics-buyer-appraise-cold",
        }), args.Console, args.Viewer, PopupType.Medium);
    }

    public double Heat(EntityUid seller)
    {
        return _sellers.TryGetValue(Key(seller), out var record) ? Cooled(record) : 0;
    }

    public double Payout(EntityUid seller, double value)
    {
        var record = _sellers.GetOrNew(Key(seller));
        return Payout(record, Cooled(record), value);
    }

    private double Payout(Seller record, double heat, double value)
    {
        return Wh40kNarcoticHeatMath.Payout(heat, value, Free(record), _cfg.GetCVar(Wh40kNarcoticsCVars.HeatScale),
            _cfg.GetCVar(Wh40kNarcoticsCVars.HeatFloor), _cfg.GetCVar(Wh40kNarcoticsCVars.HeatCap),
            _cfg.GetCVar(Wh40kNarcoticsCVars.HeatGlutFloor));
    }

    public Wh40kNarcoticHeatTier Tier(EntityUid seller)
    {
        var record = _sellers.GetOrNew(Key(seller));
        return Tier(record, Cooled(record));
    }

    private Wh40kNarcoticHeatTier Tier(Seller record, double heat)
    {
        return Wh40kNarcoticHeatMath.Tier(heat, Free(record), _cfg.GetCVar(Wh40kNarcoticsCVars.HeatHot));
    }

    private double Free(Seller record)
    {
        var settled = record.LastSale is { } last
                      && record.NerveSale != last
                      && _timing.CurTime >= last + TimeSpan.FromSeconds(_cfg.GetCVar(Wh40kNarcoticsCVars.HeatFuzzTime));
        if (record.Nerve is null || settled)
        {
            record.Nerve = _random.NextFloat(-1f, 1f);
            record.NerveSale = record.LastSale;
        }

        return _cfg.GetCVar(Wh40kNarcoticsCVars.HeatFree) * (1 + _cfg.GetCVar(Wh40kNarcoticsCVars.HeatFuzz) * record.Nerve.Value);
    }

    public double Value(HashSet<EntityUid> goods)
    {
        var value = 0.0;
        foreach (var good in goods)
        {
            value += _goods.StreetValue(good);
        }

        return value;
    }

    public string TipLine(EntityUid seller, EntityUid console, EntityUid grid, Wh40kNarcoticHeatTier tier, LocId? goods = null)
    {
        var place = Name(grid);
        var sold = Loc.GetString(goods ?? Narcotics);
        var ship = tier == Wh40kNarcoticHeatTier.Cold ? null : ShipClass(seller, console);
        if (tier == Wh40kNarcoticHeatTier.Hot)
        {
            return ship == null
                ? Loc.GetString("wh40k-narcotics-tip-hot-on-foot", ("goods", sold), ("place", place), ("who", Describe(seller)))
                : Loc.GetString("wh40k-narcotics-tip-hot", ("goods", sold), ("place", place), ("who", Describe(seller)), ("ship", ship));
        }

        return ship == null
            ? Loc.GetString("wh40k-narcotics-tip-cold", ("goods", sold), ("place", place))
            : Loc.GetString("wh40k-narcotics-tip-warm", ("goods", sold), ("place", place), ("ship", ship));
    }

    private void Tip(EntityUid seller, EntityUid console, EntityUid grid, Seller record, Wh40kNarcoticHeatTier tier, LocId? goods)
    {
        var now = _timing.CurTime;
        var cooldown = TimeSpan.FromSeconds(_cfg.GetCVar(Wh40kNarcoticsCVars.TipCooldown));
        if (record.Told is { } told && now < told + cooldown && tier <= record.ToldTier)
            return;

        record.Told = now;
        record.ToldTier = tier;
        var line = TipLine(seller, console, grid, tier, goods);
        var relay = Spawn(null, MapCoordinates.Nullspace);
        EnsureComp<TelecomExemptComponent>(relay);
        foreach (var channel in TipChannels)
        {
            if (_prototype.HasIndex(channel))
                _radio.SendRadioMessage(grid, line, channel, relay);
        }

        QueueDel(relay);
    }

    private string? ShipClass(EntityUid seller, EntityUid console)
    {
        if (!TryComp<ActorComponent>(seller, out var actor))
            return null;

        var origin = _transform.GetMapCoordinates(console);
        string? nearest = null;
        var nearestDistance = float.MaxValue;
        var query = EntityQueryEnumerator<ShipOwnershipComponent, VesselComponent, TransformComponent>();
        while (query.MoveNext(out _, out var ownership, out var vessel, out var xform))
        {
            if (ownership.OwnerUserId != actor.PlayerSession.UserId
                || xform.MapID != origin.MapId
                || !_prototype.TryIndex(vessel.VesselId, out var model))
                continue;

            var distance = (_transform.GetWorldPosition(xform) - origin.Position).LengthSquared();
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearest = model.Name;
        }

        return nearest;
    }

    private string Describe(EntityUid seller)
    {
        if (!TryComp<HumanoidAppearanceComponent>(seller, out var humanoid)
            || !_prototype.TryIndex(humanoid.Species, out var species))
            return Loc.GetString("wh40k-narcotics-tip-who-unknown");

        var name = Loc.GetString(species.Name);
        foreach (var slot in WornSlots)
        {
            if (_inventory.TryGetSlotEntity(seller, slot, out var worn))
                return Loc.GetString("wh40k-narcotics-tip-who-dressed", ("species", name), ("clothing", worn.Value));
        }

        return Loc.GetString("wh40k-narcotics-tip-who", ("species", name));
    }

    private double Cooled(Seller record)
    {
        return Wh40kNarcoticHeatMath.Cool(record.Heat, (_timing.CurTime - record.Updated).TotalSeconds,
            _cfg.GetCVar(Wh40kNarcoticsCVars.HeatCooling));
    }

    private SellerKey Key(EntityUid seller)
    {
        return TryComp<ActorComponent>(seller, out var actor)
            ? new SellerKey(actor.PlayerSession.UserId, EntityUid.Invalid)
            : new SellerKey(null, seller);
    }

    private readonly record struct SellerKey(NetUserId? User, EntityUid Body);

    private sealed class Seller
    {
        public double Heat;
        public TimeSpan Updated;
        public TimeSpan? LastSale;
        public float? Nerve;
        public TimeSpan? NerveSale;
        public TimeSpan? Told;
        public Wh40kNarcoticHeatTier ToldTier;
    }
}
