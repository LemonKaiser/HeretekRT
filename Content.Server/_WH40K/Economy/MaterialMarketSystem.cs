using System.IO;
using System.Linq;
using System.Text.Json;
using Content.Server._WH40K.SectorMap.Components;
using Content.Shared._WH40K.Economy;
using Content.Shared.CCVar;
using Content.Shared.Materials;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.Economy;

public sealed partial class MaterialMarketSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceManager _resources = default!;

    private static readonly ResPath StatePath = new("/wh40k/material-market.json");
    private static readonly ResPath BackupPath = new("/wh40k/material-market.previous.json");
    private static readonly ResPath TempPath = new("/wh40k/material-market.pending.json");
    private static readonly ProtoId<MaterialMarketProfilePrototype> ProfileId = "KoronusMaterialMarket";
    private readonly Dictionary<string, MarketLoad> _loads = new();
    private readonly Dictionary<string, double> _prices = new();
    private int _sectors;
    private bool _loaded;
    private bool _tablesReady;
    private long _revision;
    private double _observedLatheMultiplier = 1;

    public bool ObserveLatheMultiplier(double multiplier)
    {
        if (!Enabled)
            return true;
        if (!MaterialMarketMath.Valid(multiplier))
            return false;
        LoadState();
        if (multiplier >= _observedLatheMultiplier)
            return true;
        var previous = _observedLatheMultiplier;
        _observedLatheMultiplier = multiplier;
        InvalidateTables();
        if (SaveState())
            return true;
        _observedLatheMultiplier = previous;
        InvalidateTables();
        return false;
    }

    public bool Enabled => _sectors > 0 && _configuration.GetCVar(CCVars.Wh40kMaterialMarketEnabled);
    public bool SaturationEnabled => Enabled && _configuration.GetCVar(CCVars.Wh40kMaterialMarketSaturation);
    public bool DemandEnabled => Enabled && _configuration.GetCVar(CCVars.Wh40kMaterialMarketDemand);
    public MaterialMarketProfilePrototype Profile => _prototypes.Index(ProfileId);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KoronusSectorRuleComponent, ComponentStartup>((_, _, _) => _sectors++);
        SubscribeLocalEvent<KoronusSectorRuleComponent, ComponentShutdown>((_, _, _) => _sectors = Math.Max(0, _sectors - 1));
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => InvalidateTables());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _livePending.Clear();
            _recoveries.Clear();
            _nextRecovery = TimeSpan.Zero;
        });
    }

    private void InvalidateTables()
    {
        _tablesReady = false;
        _composition.Clear();
        _revision++;
    }

    public double GetUnitPrice(string material)
    {
        EnsureTables();
        if (_prices.TryGetValue(material, out var price))
            return price;
        return 0;
    }

    public double GetLoad(string material)
    {
        LoadState();
        return GetCommittedLoad(material) + _pending.Values.Sum(pending => pending.Materials.GetValueOrDefault(material));
    }

    private double GetCommittedLoad(string material)
    {
        if (!_loads.TryGetValue(material, out var state))
            return 0;
        return MaterialMarketMath.Recover(state.Volume, (_timing.CurTime - state.At).TotalSeconds, Profile.RecoveryPerSecond);
    }

    public double AppraiseMaterials(IReadOnlyDictionary<string, double> materials, double coefficient, out double ordinary,
        IReadOnlyDictionary<string, double>? loads = null)
    {
        EnsureDemandCycle();
        ordinary = 0;
        var result = 0.0;
        coefficient = Math.Clamp(coefficient, 0, Profile.MaximumCoefficient);
        foreach (var (material, quantity) in materials)
        {
            var price = GetUnitPrice(material);
            ordinary += quantity * price * coefficient;
            if (!MaterialMarketMath.TryQuote(SaturationEnabled ? loads?.GetValueOrDefault(material) ?? GetLoad(material) : 0,
                    quantity, price, coefficient, SaturationEnabled ? Profile.GraceVolume : double.MaxValue,
                    Profile.PressureScale, Profile.MinimumFactor, out var amount, GetDemandFactor(material)))
                return 0;
            result += amount;
        }
        return double.IsFinite(result) ? result : 0;
    }

    public bool IsCurrent(MaterialMarketQuote quote)
    {
        if (!Enabled || !EnsureDemandCycle() || quote.Revision != _revision || quote.Cycle != DemandCycleId || quote.Amount < 0 ||
            quote.Saturation != SaturationEnabled || _timing.CurTime < quote.CreatedAt ||
            _timing.CurTime - quote.CreatedAt > TimeSpan.FromSeconds(30) ||
            !MaterialMarketMath.Valid(quote.FixedAmount) || !MaterialMarketMath.Valid(quote.Coefficient) ||
            quote.Coefficient > Profile.MaximumCoefficient || quote.Materials.Any(pair => !MaterialMarketMath.Valid(pair.Value)))
            return false;
        var amount = quote.FixedAmount * quote.Coefficient + AppraiseMaterials(quote.Materials, quote.Coefficient, out _, quote.Loads);
        return double.IsFinite(amount) && MaterialMarketMath.ToCash(amount) == quote.Amount;
    }

    public bool Commit(MaterialMarketQuote quote)
    {
        if (!IsCurrent(quote))
            return false;
        if (!SaturationEnabled || quote.Materials.Count == 0 || quote.Amount == 0)
            return true;

        var before = new Dictionary<string, MarketLoad>(_loads);
        foreach (var (material, quantity) in quote.Materials)
            _loads[material] = new MarketLoad(GetCommittedLoad(material) + quantity, _timing.CurTime);
        if (!SaveState())
        {
            _loads.Clear();
            foreach (var (key, state) in before)
                _loads.Add(key, state);
            return false;
        }
        _revision++;
        return true;
    }

    public bool TryTransaction(MaterialMarketQuote quote, Func<bool> approve)
    {
        var previous = new Dictionary<string, MarketLoad>(_loads);
        if (!Commit(quote))
            return false;
        var success = false;
        try
        {
            success = approve();
            return success;
        }
        finally
        {
            if (!success)
            {
                _loads.Clear();
                foreach (var (key, load) in previous)
                    _loads[key] = load;
                _revision++;
                SaveState();
            }
        }
    }

    private void LoadState()
    {
        if (_loaded)
            return;
        _loaded = true;
        foreach (var path in new[] { StatePath, BackupPath })
        {
            if (!_resources.UserData.Exists(path))
                continue;
            try
            {
                var data = JsonSerializer.Deserialize<MarketSnapshot>(_resources.UserData.ReadAllText(path));
                if (data is not { Version: 1 } || data.Loads == null)
                    throw new InvalidDataException("Unsupported material market state.");
                if (data.Demand is { } demand && demand.Id != Guid.Empty && demand.Factors != null &&
                    demand.EndsAtUtc <= DateTime.UtcNow.AddSeconds(Profile.DemandCycleSeconds * 2) &&
                    demand.Factors.All(pair => _prototypes.HasIndex<MaterialPrototype>(pair.Key) &&
                        double.IsFinite(pair.Value) && pair.Value >= Profile.MinimumFactor && pair.Value <= Profile.DemandBonusMaximum))
                    _demandCycle = demand;
                if (MaterialMarketMath.Valid(data.LatheMultiplier) && data.LatheMultiplier <= 1)
                    _observedLatheMultiplier = Math.Min(_observedLatheMultiplier, data.LatheMultiplier);
                foreach (var (id, state) in data.Loads)
                {
                    if (state == null || !_prototypes.HasIndex<MaterialPrototype>(id) || !MaterialMarketMath.Valid(state.Volume))
                        continue;
                    var elapsed = Math.Max(0, (DateTime.UtcNow - state.AtUtc).TotalSeconds);
                    _loads[id] = new MarketLoad(MaterialMarketMath.Recover(state.Volume, elapsed, Profile.RecoveryPerSecond), _timing.CurTime);
                }
                foreach (var pending in data.Pending ?? new())
                    if (pending != null && pending.Operation != Guid.Empty && pending.ItemToken != Guid.Empty && pending.Materials != null &&
                        pending.Materials.All(pair => _prototypes.HasIndex<MaterialPrototype>(pair.Key) && MaterialMarketMath.Valid(pair.Value)))
                        _pending[pending.Operation] = pending;
                return;
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidDataException or ArgumentException)
            {
                Log.Error($"Cannot load material market state {path}: {error.Message}");
            }
        }
    }

    public bool SaveState()
    {
        LoadState();
        try
        {
            var snapshot = new MarketSnapshot(1, _loads.ToDictionary(pair => pair.Key,
                pair => new SavedLoad(GetCommittedLoad(pair.Key), DateTime.UtcNow)), _pending.Values.ToList(), _observedLatheMultiplier,
                _demandCycle);
            _resources.UserData.CreateDir(StatePath.Directory);
            using (var writer = _resources.UserData.OpenWriteText(TempPath))
                writer.Write(JsonSerializer.Serialize(snapshot));
            if (_resources.UserData.Exists(BackupPath))
                _resources.UserData.Delete(BackupPath);
            if (_resources.UserData.Exists(StatePath))
                _resources.UserData.Rename(StatePath, BackupPath);
            _resources.UserData.Rename(TempPath, StatePath);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Cannot save material market state: {error.Message}");
            return false;
        }
    }

    public bool ResetDemand(string? material = null)
    {
        LoadState();
        if (_pending.Count != 0)
            return false;
        var previous = new Dictionary<string, MarketLoad>(_loads);
        if (material == null)
            _loads.Clear();
        else
            _loads.Remove(material);
        if (!SaveState())
        {
            _loads.Clear();
            foreach (var (key, value) in previous)
                _loads[key] = value;
            return false;
        }
        _revision++;
        return true;
    }

    public bool ReloadState()
    {
        if (_pending.Count != 0)
            return false;
        _loads.Clear();
        _demandCycle = null;
        _loaded = false;
        LoadState();
        InvalidateTables();
        return true;
    }

    private sealed record MarketLoad(double Volume, TimeSpan At);
    public sealed record SavedLoad(double Volume, DateTime AtUtc);
    public sealed record MarketSnapshot(int Version, Dictionary<string, SavedLoad> Loads,
        List<PendingSale>? Pending = null, double LatheMultiplier = 1, MarketDemandCycle? Demand = null);
}

public sealed class MaterialMarketQuote
{
    public Guid Id = Guid.NewGuid();
    public long Revision;
    public Guid Cycle;
    public TimeSpan CreatedAt;
    public bool Saturation;
    public double Coefficient;
    public double FixedAmount;
    public double BeforeCoefficient;
    public double OrdinaryAmount;
    public int CashAmount;
    public int GrossAmount;
    public int Amount;
    public readonly Dictionary<string, double> Materials = new();
    public readonly Dictionary<string, double> Loads = new();
    public readonly HashSet<EntityUid> Sold = new();
    public readonly HashSet<EntityUid> Seen = new();
    public readonly HashSet<EntityUid> Roots = new();
    public readonly Dictionary<EntityUid, double> SpecialPrices = new();
    public readonly Dictionary<EntityUid, double> OwnValues = new();
    public readonly Dictionary<EntityUid, Dictionary<string, double>> OwnMaterials = new();
}
