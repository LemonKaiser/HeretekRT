using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.WolfChem.Narcotics;

/// <summary>
/// Round-long dependence per drug family. Lives on the body, so cloning or a new body starts clean.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kDependenceComponent : Component
{
    /// <summary>Dependence per family, 0-1.</summary>
    [DataField]
    public Dictionary<Wh40kDrugFamily, float> Meter = new();

    /// <summary>The meter won't decay below this, from a trait.</summary>
    [DataField]
    public Dictionary<Wh40kDrugFamily, float> Floor = new();

    /// <summary>Tolerance per family, 0-1. Speeds up decay by up to x2.</summary>
    [DataField]
    public Dictionary<Wh40kDrugFamily, float> Tolerance = new();

    /// <summary>Seconds since each family was last above its first tier.</summary>
    [DataField]
    public Dictionary<Wh40kDrugFamily, float> SinceUse = new();

    /// <summary>The withdrawal stage each family is in, 0 for none.</summary>
    [DataField]
    public Dictionary<Wh40kDrugFamily, int> Stage = new();

    /// <summary>Seconds until the next craving line per family.</summary>
    [DataField]
    public Dictionary<Wh40kDrugFamily, float> NextCraving = new();

    /// <summary>Families the body depends on and is high on now.</summary>
    [DataField]
    public HashSet<Wh40kDrugFamily> Satisfied = new();
}

/// <summary>
/// Starts the round dependent on one family, from a trait. One subclass per family so several traits can stack.
/// </summary>
public abstract partial class Wh40kDependentTraitComponent : Component
{
    public abstract Wh40kDrugFamily Family { get; }

    /// <summary>The meter at round start.</summary>
    [DataField]
    public float Start = 0.8f;

    /// <summary>The meter never decays below this.</summary>
    [DataField]
    public float Floor = 0.55f;

    /// <summary>The first fix put in an organic body's hand.</summary>
    [DataField]
    public EntProtoId? Gear;

    /// <summary>The first fix put in a neuroware host's hand instead: the family's program on a stick.</summary>
    [DataField]
    public EntProtoId? HostGear;
}

[RegisterComponent]
public sealed partial class Wh40kDependentStimulantComponent : Wh40kDependentTraitComponent
{
    public override Wh40kDrugFamily Family => Wh40kDrugFamily.Stimulant;
}

[RegisterComponent]
public sealed partial class Wh40kDependentOpioidComponent : Wh40kDependentTraitComponent
{
    public override Wh40kDrugFamily Family => Wh40kDrugFamily.Opioid;
}

[RegisterComponent]
public sealed partial class Wh40kDependentNicotineComponent : Wh40kDependentTraitComponent
{
    public override Wh40kDrugFamily Family => Wh40kDrugFamily.Nicotine;
}

[RegisterComponent]
public sealed partial class Wh40kDependentEmpathogenComponent : Wh40kDependentTraitComponent
{
    public override Wh40kDrugFamily Family => Wh40kDrugFamily.Empathogen;
}

[RegisterComponent]
public sealed partial class Wh40kDependentCannabinoidComponent : Wh40kDependentTraitComponent
{
    public override Wh40kDrugFamily Family => Wh40kDrugFamily.Cannabinoid;
}
