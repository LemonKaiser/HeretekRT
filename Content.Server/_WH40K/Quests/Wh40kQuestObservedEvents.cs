using Content.Shared._WH40K.Activities;

namespace Content.Server._WH40K.Quests;

/// <summary>Server producer boundaries only; these events are never client requests.</summary>
public sealed record Wh40kQuestActivityOutcomeEvent(long Instance, string Template, string System,
    KoronusActivityTerminalReason Reason, EntityUid? Actor, EntityUid? Recovered);
public sealed record Wh40kQuestExtractionEvent(EntityUid Vein, EntityUid? Actor, string Ore, int Amount);
public sealed record Wh40kQuestIntakeEvent(EntityUid Receiver, EntityUid Root, EntityUid? Actor);
