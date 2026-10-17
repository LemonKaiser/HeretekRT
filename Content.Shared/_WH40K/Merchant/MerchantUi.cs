using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Merchant;

[Serializable, NetSerializable]
public enum MerchantUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class MerchantOfferState
{
    public string Product;
    public int Price;
    public int Remaining;
    public int MaxStock;
    public string? DisplayName;
    public string? Description;

    public MerchantOfferState(
        string product,
        int price,
        int remaining,
        int maxStock,
        string? displayName,
        string? description)
    {
        Product = product;
        Price = price;
        Remaining = remaining;
        MaxStock = maxStock;
        DisplayName = displayName;
        Description = description;
    }
}

[Serializable, NetSerializable]
public sealed class MerchantUpdateState : BoundUserInterfaceMessage
{
    public Guid CatalogId;
    public bool Buyback;
    public int Balance;
    public string Title;
    public string Greeting;
    public float RefreshInSeconds;
    public int RefreshNumber;
    public List<MerchantOfferState> Offers;

    public MerchantUpdateState(
        string title,
        string greeting,
        float refreshInSeconds,
        int refreshNumber,
        List<MerchantOfferState> offers,
        Guid catalogId,
        bool buyback = false,
        int balance = 0)
    {
        Title = title;
        Greeting = greeting;
        RefreshInSeconds = refreshInSeconds;
        RefreshNumber = refreshNumber;
        Offers = offers;
        CatalogId = catalogId;
        Buyback = buyback;
        Balance = balance;
    }
}

[Serializable, NetSerializable]
public sealed class MerchantBuyMessage : BoundUserInterfaceMessage
{
    public string Product;
    public Guid CatalogId;
    public Guid Operation;

    public MerchantBuyMessage(string product, Guid catalogId, Guid operation)
    {
        Product = product;
        CatalogId = catalogId;
        Operation = operation;
    }
}

[Serializable, NetSerializable]
public sealed class MerchantRequestRefreshMessage : BoundUserInterfaceMessage
{
}
