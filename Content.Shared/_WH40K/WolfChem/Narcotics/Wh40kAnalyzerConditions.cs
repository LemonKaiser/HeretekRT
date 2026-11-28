using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.WolfChem.Narcotics;

/// <summary>
/// The health analyzer's conditions readout: drug levels, status effects something is holding, crashes and
/// dependence, each with what clears it.
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kAnalyzerConditions
{
    public List<Wh40kAnalyzerDrug> Drugs = new();
    public List<Wh40kAnalyzerHeld> Held = new();
    public List<Wh40kAnalyzerCrash> Crashes = new();
    public List<Wh40kAnalyzerHabit> Habits = new();
}

/// <summary>
/// A reagent behind an effect: how much is in the body, about how long it takes to clear (negative when nothing
/// processes it), and the reagents that remove it faster.
/// </summary>
[Serializable, NetSerializable]
public sealed record Wh40kAnalyzerCause(string Reagent, FixedPoint2 Quantity, float ClearSeconds, bool Stomach,
    List<string> RemovedBy);

/// <summary>A drug level, the reagents raising it and the antidotes that bring its family down.</summary>
[Serializable, NetSerializable]
public sealed record Wh40kAnalyzerDrug(Wh40kDrugFamily Family, Wh40kDrugPhase Phase, List<Wh40kAnalyzerCause> Causes,
    List<string> Antidotes);

/// <summary>What keeps a status effect going besides a reagent.</summary>
[Serializable, NetSerializable]
public enum Wh40kAnalyzerSourceKind : byte
{
    High,
    Crash,
    Withdrawal,
}

[Serializable, NetSerializable]
public readonly record struct Wh40kAnalyzerSource(Wh40kAnalyzerSourceKind Kind, Wh40kDrugFamily Family);

/// <summary>A status effect, its time left, and the reagents and conditions holding it.</summary>
[Serializable, NetSerializable]
public sealed record Wh40kAnalyzerHeld(string Key, float SecondsLeft, List<Wh40kAnalyzerCause> Causes,
    List<Wh40kAnalyzerSource> Sources);

/// <summary>A running crash and its time left.</summary>
[Serializable, NetSerializable]
public readonly record struct Wh40kAnalyzerCrash(Wh40kDrugFamily Family, float SecondsLeft);

/// <summary>
/// Dependence on one family. <see cref="WithdrawalIn"/> is the time until withdrawal without a dose, and
/// <see cref="CleanIn"/> the time clean until the habit is too weak for withdrawal; each is negative when it
/// doesn't apply, and <see cref="CleanIn"/> is infinite when a trait keeps the habit up.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct Wh40kAnalyzerHabit(Wh40kDrugFamily Family, float Meter, float Floor, float Tolerance,
    int Stage, float WithdrawalIn, float CleanIn);
