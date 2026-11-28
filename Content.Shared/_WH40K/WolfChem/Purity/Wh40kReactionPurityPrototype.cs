using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.WolfChem.Purity;

/// <summary>
/// How well a reaction keeps its product's grade, keyed by the reaction's ID. A reaction with none passes its input
/// grade through.
/// </summary>
[Prototype]
public sealed partial class Wh40kReactionPurityPrototype : IPrototype
{
    /// <summary>The reaction's ID.</summary>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Temperature, in kelvin, at which the product keeps its full input grade. Null keeps it at any temperature.
    /// </summary>
    [DataField]
    public float? OptimalTemp;

    /// <summary>Kelvin from the optimum at which the factor reaches <see cref="Floor"/>.</summary>
    [DataField]
    public float Tolerance;

    /// <summary>Reactants that count as grade zero, so a filler cuts the product's grade by the volume it adds.</summary>
    [DataField]
    public HashSet<ProtoId<ReagentPrototype>> Fillers = new();

    /// <summary>The lowest process factor.</summary>
    [DataField]
    public float Floor = 0.4f;

    /// <summary>Most kelvin the optimum moves either way each round.</summary>
    [DataField]
    public float Drift;

    /// <summary>
    /// The process factor at a temperature, falling linearly from 1.0 at the optimum, moved by
    /// <paramref name="shift"/> kelvin, to the floor.
    /// </summary>
    public float Factor(float temperature, float shift = 0f)
    {
        if (OptimalTemp is not { } optimal)
            return 1f;

        optimal += shift;

        if (Tolerance <= 0f)
            return MathF.Abs(temperature - optimal) < 0.01f ? 1f : Floor;

        var off = Math.Min(MathF.Abs(temperature - optimal) / Tolerance, 1f);
        return 1f - (1f - Floor) * off;
    }
}
