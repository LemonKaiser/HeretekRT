using System.Linq;
using Content.Shared._WH40K.Progression;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Progression;

/// <summary>
/// Trusted server request produced by a gameplay adapter.
/// </summary>
public sealed record Wh40kExperienceEventRequest(
    ProtoId<Wh40kExperienceSourcePrototype> Source,
    string RewardId,
    NetUserId? PrimaryUserId = null,
    NetUserId? TargetUserId = null,
    EntityCoordinates? EventCoordinates = null,
    EntityUid? Grid = null,
    IReadOnlySet<MapId>? Maps = null,
    int DifficultyMultiplier = Wh40kExperiencePolicy.FullMultiplier,
    string? IssuerEntity = null,
    string? ContextJson = null);

public sealed record Wh40kExperienceEventResult(
    IReadOnlyList<Wh40kExperienceAwardResult> Awards)
{
    public long AwardedTenths => Awards
        .Where(result => result.IsAwarded)
        .Sum(result => result.Ledger.AmountTenths);
}
