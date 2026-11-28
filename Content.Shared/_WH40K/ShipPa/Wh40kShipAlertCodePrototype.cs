using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.ShipPa;

/// <summary>
/// One alert condition code a ship can broadcast over its Vox-PA network.
/// </summary>
[Prototype]
public sealed partial class Wh40kShipAlertCodePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField(required: true)] public LocId Name;
    [DataField] public LocId? Description;
    [DataField(required: true)] public LocId Announcement;
    [DataField] public Color Color = Color.White;
    [DataField] public int Order;
    [DataField] public bool Selectable = true;
    [DataField] public SoundSpecifier? Sound;
}
