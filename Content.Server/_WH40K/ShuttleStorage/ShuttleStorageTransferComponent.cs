namespace Content.Server._WH40K.ShuttleStorage;

[RegisterComponent]
public sealed partial class ShuttleStorageTransferComponent : Component
{
    // Runtime-only: a deserialized grid is not owned by an old in-memory operation.
    public bool Active;

    [DataField]
    public string ShipId = Guid.NewGuid().ToString("N");

    public Guid Id => Guid.TryParse(ShipId, out var id) ? id : Guid.Empty;
}

[RegisterComponent]
public sealed partial class ShuttleStorageStagingComponent : Component;
