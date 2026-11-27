namespace Content.Shared._WH40K.Cyborgs;

/// <summary>
/// A cyborg module stack that restocks itself from its cyborg's power cell.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kBorgStackRefillComponent : Component
{
    /// <summary>
    /// The count the cell refills the stack to. Picking up more by hand can go past it.
    /// </summary>
    [DataField]
    public int Target = 15;

    /// <summary>
    /// Cell charge each restocked unit costs, in joules.
    /// </summary>
    [DataField]
    public float ChargePerUnit = 20f;
}
