using System.IO;
using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;
using Content.Shared.Materials;

namespace Content.Shared._WH40K.Economy;

[Prototype]
public sealed partial class MaterialMarketProfilePrototype : IPrototype, ISerializationHooks
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public double MaximumCoefficient = 1.5;
    [DataField] public double MinimumRetailMarkup = 1.25;
    [DataField] public double ResaleSafetyMarkup = 1.1;
    [DataField] public double GraceVolume = 20000;
    [DataField] public double PressureScale = 10000;
    [DataField] public double RecoveryPerSecond = 17;
    [DataField] public double MinimumFactor = 0.4;
    [DataField] public double LatheMaterialRate = 0.5;
    [DataField] public double DemandCycleSeconds = 1200;
    [DataField] public int DemandRisingCount = 2;
    [DataField] public int DemandFallingCount = 2;
    [DataField] public double DemandBonusMinimum = 1.2;
    [DataField] public double DemandBonusMaximum = 1.45;
    [DataField] public double DemandDiscountMinimum = 0.7;
    [DataField] public double DemandDiscountMaximum = 0.9;
    [DataField] public List<ProtoId<MaterialPrototype>> DemandMaterials = new();
    [DataField(customTypeSerializer: typeof(PrototypeIdDictionarySerializer<double, MaterialPrototype>))]
    public Dictionary<string, double> Prices = new();

    void ISerializationHooks.AfterDeserialization()
    {
        if (!double.IsFinite(MaximumCoefficient) || MaximumCoefficient <= 0 ||
            !double.IsFinite(MinimumRetailMarkup) || MinimumRetailMarkup <= 1 ||
            !double.IsFinite(ResaleSafetyMarkup) || ResaleSafetyMarkup <= 1 ||
            !MaterialMarketMath.Valid(GraceVolume) || !double.IsFinite(PressureScale) || PressureScale <= 0 ||
            !double.IsFinite(RecoveryPerSecond) || RecoveryPerSecond <= 0 ||
            !double.IsFinite(MinimumFactor) || MinimumFactor <= 0 || MinimumFactor > 1 ||
            !double.IsFinite(LatheMaterialRate) || LatheMaterialRate <= 0 || LatheMaterialRate > 1 ||
            !double.IsFinite(DemandCycleSeconds) || DemandCycleSeconds < 60 || DemandCycleSeconds > 86400 ||
            DemandRisingCount < 0 || DemandFallingCount < 0 ||
            DemandRisingCount + DemandFallingCount > DemandMaterials.Distinct().Count() ||
            !double.IsFinite(DemandBonusMinimum) || !double.IsFinite(DemandBonusMaximum) ||
            DemandBonusMinimum < 1 || DemandBonusMaximum < DemandBonusMinimum || DemandBonusMaximum > 3 ||
            !double.IsFinite(DemandDiscountMinimum) || !double.IsFinite(DemandDiscountMaximum) ||
            DemandDiscountMinimum < MinimumFactor || DemandDiscountMaximum < DemandDiscountMinimum || DemandDiscountMaximum > 1 ||
            Prices.Values.Any(price => !MaterialMarketMath.Valid(price)))
            throw new InvalidDataException($"Invalid material market profile {ID}.");
    }
}
