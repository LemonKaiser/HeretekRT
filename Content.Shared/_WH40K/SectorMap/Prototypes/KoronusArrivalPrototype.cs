using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.SectorMap.Prototypes;

[Prototype("koronusArrival")]
public sealed partial class KoronusArrivalPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public KoronusArrivalKind Kind;

    [DataField]
    public Color Color = Color.FromHex("#8E44AD");

    // Paths relative to Resources. One individual map or one complete group is selected per event.
    [DataField]
    public List<string> Maps = new();

    [DataField]
    public List<KoronusArrivalGroup> Groups = new();

    [DataField(required: true)]
    public LocId Announcement;

    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    [DataField]
    public float MinInterval = 1800f;

    [DataField]
    public float MaxInterval = 7200f;

    [DataField]
    public float MinLifetime = 1200f;

    [DataField]
    public float MaxLifetime = 1800f;

    [DataField]
    public float WarningTime = 30f;
}

public enum KoronusArrivalKind : byte
{
    Object,
    Tau,
}

[DataDefinition]
public sealed partial class KoronusArrivalGroup
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField(required: true)]
    public List<string> Maps = new();
}
