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
