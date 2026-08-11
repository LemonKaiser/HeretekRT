using Content.Shared.Salvage;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Progression;

public readonly record struct Wh40kSalvageExpeditionCompletedEvent(
    MapId ExpeditionMap,
    DifficultyRating Difficulty,
    int Seed);

public readonly record struct Wh40kPlanetaryLandingCompletedEvent(
    string BodyId,
    string SourceId,
    MapId SurfaceMap);

public readonly record struct Wh40kUsefulHealingCompletedEvent(
    EntityUid Helper,
    EntityUid Target,
    float HealedDamage);

/// <summary>
/// Directed, server-only hook raised on the healer before starting and completing a real medical-item use.
/// Class systems may only adjust the bounded multipliers; the medical system still owns validation,
/// DoAfter interruption and item consumption.
/// </summary>
public sealed class Wh40kClassHealingAttemptEvent(
    EntityUid used,
    EntityUid target,
    bool completing) : EntityEventArgs
{
    public readonly EntityUid Used = used;
    public readonly EntityUid Target = target;
    public readonly bool Completing = completing;
    public float DelayMultiplier = 1f;
    public float HealingMultiplier = 1f;
    public float BloodlossMultiplier = 1f;
}
