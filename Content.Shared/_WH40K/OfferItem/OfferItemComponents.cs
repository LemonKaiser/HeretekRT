using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.OfferItem;

/// <summary>
/// State kept on the player offering an item.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OfferingItemComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Target;

    [DataField, AutoNetworkedField]
    public EntityUid? Item;

    [DataField]
    public TimeSpan ExpiresAt;
}

/// <summary>
/// State kept on the player that may accept an offered item.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OfferedItemComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Source;

    [DataField, AutoNetworkedField]
    public EntityUid? Item;
}

[Serializable, NetSerializable]
public sealed class RequestOfferItemEvent(NetEntity target, NetEntity item) : EntityEventArgs
{
    public readonly NetEntity Target = target;
    public readonly NetEntity Item = item;
}

[Serializable, NetSerializable]
public sealed class AcceptOfferedItemEvent(NetEntity source) : EntityEventArgs
{
    public readonly NetEntity Source = source;
}
