using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.WolfChem.Neuroware;

/// <summary>Appearance keys for a neuroware stick.</summary>
[Serializable, NetSerializable]
public enum Wh40kNeurowareStickVisuals : byte
{
    Copied,
}

/// <summary>
/// A data stick holding one drug profile's neuroware twin. Loading it into a host runs the program; using it on a
/// blank stick copies it, a little worse each generation, and a blank used on a drug reads it in.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kNeurowareStickComponent : Component
{
    [DataField]
    public ProtoId<Wh40kDrugProfilePrototype>? Profile;

    [DataField]
    public float Integrity = 1f;

    [DataField]
    public int Loads = 3;

    [DataField]
    public float? Amount;

    [DataField]
    public float Rate = 0.5f;

    [DataField]
    public TimeSpan SelfDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan OtherDelay = TimeSpan.FromSeconds(4);

    [DataField]
    public TimeSpan EncodeDelay = TimeSpan.FromSeconds(3);
}

/// <summary>Neuroware running on a host body.</summary>
[RegisterComponent]
public sealed partial class Wh40kNeurowareComponent : Component
{
    [DataField]
    public List<Wh40kNeurowareProgram> Programs = new();

    [DataField]
    public LocId? FalseFault;

    [DataField]
    public TimeSpan FalseFaultUntil;
}

/// <summary>One program delivering its profile's level.</summary>
[DataDefinition]
public sealed partial class Wh40kNeurowareProgram
{
    [DataField(required: true)]
    public ProtoId<Wh40kDrugProfilePrototype> Profile;

    [DataField]
    public float Remaining;

    [DataField]
    public float Rate;

    [DataField]
    public float Integrity = 1f;
}

[Serializable, NetSerializable]
public sealed partial class Wh40kNeurowareLoadDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class Wh40kNeurowareEncodeDoAfterEvent : SimpleDoAfterEvent;
