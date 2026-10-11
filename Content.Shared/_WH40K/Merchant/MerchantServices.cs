using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared._WH40K.SectorMap.BUI;

namespace Content.Shared._WH40K.Merchant;

[Prototype("wh40kMerchantValuation")]
public sealed partial class MerchantValuationPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public Dictionary<EntProtoId, int> Prices = new();
}

/// <summary>Stable title for approved resale goods, including copies restored from older inventory snapshots.</summary>
[RegisterComponent]
public sealed partial class MerchantItemTokenComponent : Component
{
    [ViewVariables] public Guid Token;
    [ViewVariables] public bool Verified = true;
}

[RegisterComponent]
public sealed partial class Wh40kTransitInfoComponent : Component;

[Serializable, NetSerializable]
public enum Wh40kTransitUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class Wh40kTransitState(KoronusSectorInterfaceState sector) : BoundUserInterfaceState
{
    public KoronusSectorInterfaceState Sector = sector;
}

[Serializable, NetSerializable]
public sealed class Wh40kTransitRefresh : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class MerchantAppraiseMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class MerchantSellMessage(Guid quote, Guid operation) : BoundUserInterfaceMessage
{
    public Guid Quote = quote;
    public Guid Operation = operation;
}

[Serializable, NetSerializable]
public sealed class MerchantAppraisalState(Guid quote, string product, int price) : BoundUserInterfaceMessage
{
    public Guid Quote = quote;
    public string Product = product;
    public int Price = price;
}

[Serializable, NetSerializable]
public sealed class MerchantResultMessage(Guid operation, string result) : BoundUserInterfaceMessage
{
    public Guid Operation = operation;
    public string Result = result;
}
