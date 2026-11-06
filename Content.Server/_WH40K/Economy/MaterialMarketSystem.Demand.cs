using System.Linq;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Economy;

public sealed partial class MaterialMarketSystem
{
    [Dependency] private IRobustRandom _random = default!;
    private MarketDemandCycle? _demandCycle;

    public Guid DemandCycleId => DemandEnabled ? _demandCycle?.Id ?? Guid.Empty : Guid.Empty;
    public int DemandSecondsRemaining => !DemandEnabled || _demandCycle == null ? 0 :
        (int) Math.Clamp(Math.Ceiling((_demandCycle.EndsAtUtc - DateTime.UtcNow).TotalSeconds), 0, Profile.DemandCycleSeconds);

    private bool EnsureDemandCycle()
    {
        if (!DemandEnabled)
            return true;
        LoadState();
        if (_demandCycle != null && _demandCycle.EndsAtUtc > DateTime.UtcNow)
            return true;

        var materials = Profile.DemandMaterials.Select(id => id.Id).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
        _random.Shuffle(materials);
        var factors = new Dictionary<string, double>();
        var index = 0;
        for (; index < Math.Min(Profile.DemandRisingCount, materials.Count); index++)
            factors[materials[index]] = Profile.DemandBonusMinimum + _random.NextDouble() *
                (Profile.DemandBonusMaximum - Profile.DemandBonusMinimum);
        var end = Math.Min(materials.Count, index + Profile.DemandFallingCount);
        for (; index < end; index++)
            factors[materials[index]] = Profile.DemandDiscountMinimum + _random.NextDouble() *
                (Profile.DemandDiscountMaximum - Profile.DemandDiscountMinimum);

        var previous = _demandCycle;
        _demandCycle = new MarketDemandCycle(Guid.NewGuid(), DateTime.UtcNow.AddSeconds(Profile.DemandCycleSeconds), factors);
        if (!SaveState())
        {
            _demandCycle = previous;
            return false;
        }
        _revision++;
        return true;
    }

    public double GetDemandFactor(string material)
    {
        EnsureDemandCycle();
        return DemandEnabled && _demandCycle != null && _demandCycle.EndsAtUtc > DateTime.UtcNow
            ? Math.Clamp(_demandCycle.Factors.GetValueOrDefault(material, 1), Profile.MinimumFactor, Profile.DemandBonusMaximum) : 1;
    }

    public sealed record MarketDemandCycle(Guid Id, DateTime EndsAtUtc, Dictionary<string, double> Factors);
}
