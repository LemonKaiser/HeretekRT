using Robust.Shared.Prototypes;
using Robust.Shared.Map;

namespace Content.Shared._WH40K.Merchant;

/// <summary>
/// A configurable NPC merchant. The offer pool is authored in YAML while the
/// current assortment is generated and owned by the server.
/// </summary>
[RegisterComponent]
public sealed partial class MerchantComponent : Component
{
    [DataField("title")]
    public LocId Title = "wh40k-merchant-title";

    [DataField("greeting")]
    public LocId Greeting = "wh40k-merchant-greeting";

    [DataField("refreshInterval")]
    public TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    [DataField("displayCount")]
    public int DisplayCount = 15;

    [DataField("interactionRange")]
    public float InteractionRange = 3f;

    [DataField("lookAtRange")]
    public float LookAtRange = 1f;

    [DataField("returnDistance")]
    public float ReturnDistance = 3f;

    [DataField("offers")]
    public List<MerchantOfferPrototype> Offers = new();

    [ViewVariables]
    public List<MerchantStockEntry> CurrentStock = new();

    [ViewVariables]
    public TimeSpan NextRefreshAt;

    [ViewVariables]
    public int RefreshNumber;

    [ViewVariables]
    public EntityCoordinates? HomeCoordinates;

    public Guid CatalogId = Guid.NewGuid();
    public Dictionary<(Guid User, Guid Operation), (string Product, string Result)> Receipts = new();

    [DataField] public ProtoId<MerchantValuationPrototype>? Buyback;
}

[RegisterComponent]
public sealed partial class MerchantSpawnMarkerComponent : Component
{
}

[DataDefinition]
public sealed partial class MerchantOfferPrototype
{
    [DataField("product", required: true)]
    public EntProtoId Product = default!;

    [DataField("price")]
    public int Price = 25;

    [DataField("minStock")]
    public int MinStock = 1;

    [DataField("maxStock")]
    public int MaxStock = 3;

    [DataField("weight")]
    public int Weight = 1;

    [DataField("displayName")]
    public LocId? DisplayName;

    [DataField("description")]
    public LocId? Description;

    [DataField] public ProtoId<Content.Shared.Access.AccessLevelPrototype>? RequiredAccess;
}

public sealed class MerchantStockEntry
{
    public MerchantOfferPrototype Offer { get; }
    public int InitialStock { get; }
    public int Remaining { get; set; }

    public MerchantStockEntry(MerchantOfferPrototype offer, int remaining)
    {
        Offer = offer;
        InitialStock = remaining;
        Remaining = remaining;
    }
}
