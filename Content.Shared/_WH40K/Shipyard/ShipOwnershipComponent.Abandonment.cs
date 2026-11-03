namespace Content.Shared._NF.Shipyard.Components;

public sealed partial class ShipOwnershipComponent
{
    /// <summary>
    /// Cryosleep is an explicit departure even while the account remains connected.
    /// </summary>
    [DataField]
    public bool InCryosleep;

    /// <summary>
    /// Credits an automatic sale to the character who purchased the ship.
    /// </summary>
    [DataField]
    public int? OwnerCharacterSlot;
}
