using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Merchant;

/// <summary>Configuration for a paid, self-only medical service; no items are sold by this component.</summary>
[RegisterComponent]
public sealed partial class Wh40kMedicalProviderComponent : Component
{
    [DataField] public int BasePrice = 50;
    [DataField] public int PricePerDamage = 4;
    [DataField] public int CooldownSeconds = 600;
}

public static class Wh40kMedicalTreatment
{
    // One existing account-state row; UTC seconds survive bodies, rounds and server restarts.
    public const string CooldownKey = "medical:treatment-cooldown";
}

[Serializable, NetSerializable]
public enum Wh40kMedicalUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class Wh40kMedicalRefresh : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class Wh40kMedicalConfirm(Guid quote, Guid operation) : BoundUserInterfaceMessage
{
    public Guid Quote = quote;
    public Guid Operation = operation;
}

/// <summary>A private server quote. The client never supplies a price, damage value or patient.</summary>
[Serializable, NetSerializable]
public sealed class Wh40kMedicalQuoteMessage(Guid quote, int damage, int price, int balance, int cooldown, string status)
    : BoundUserInterfaceMessage
{
    public Guid Quote = quote;
    public int Damage = damage;
    public int Price = price;
    public int Balance = balance;
    public int Cooldown = cooldown;
    public string Status = status;
}

[Serializable, NetSerializable]
public sealed class Wh40kMedicalResult(Guid operation, string result) : BoundUserInterfaceMessage
{
    public Guid Operation = operation;
    public string Result = result;
}
