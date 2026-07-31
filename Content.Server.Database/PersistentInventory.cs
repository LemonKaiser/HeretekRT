using System;
using System.Collections.Generic;

namespace Content.Server.Database;

/// <summary>
/// Account snapshot state. <see cref="None"/> is represented by the absence of a database row.
/// </summary>
public enum PersistentInventorySnapshotState
{
    None = 0,
    Staging = 1,
    Active = 2,
    Bound = 3,
    Invalid = 4,
    LostByDisconnect = 5,
    Aborted = 6,
    Quarantined = 7,
}

public enum PersistentInventoryInvalidationReason
{
    None = 0,
    Surrender = 1,
    Suicide = 2,
    VoluntaryGhost = 3,
    Gib = 4,
    BodyDeleted = 5,
    StaffAction = 6,
}

public enum PersistentInventoryLossReason
{
    None = 0,
    DisconnectTimeout = 1,
    ServerRecovery = 2,
}

public enum PersistentInventoryQuarantineReason
{
    None = 0,
    AmbiguousRecovery = 1,
    HashMismatch = 2,
    InvalidSchema = 3,
    SizeLimit = 4,
    MissingPrototype = 5,
    UnsafeReference = 6,
    DatabaseInvariant = 7,
    StaffAction = 8,
}

public enum PersistentInventoryAuditAction
{
    Staged = 1,
    Promoted = 2,
    StateChanged = 3,
    WorldCleanupAuthorized = 4,
    Invalidated = 5,
    Lost = 6,
    Quarantined = 7,
    RolledBack = 8,
    Recovered = 9,
    Repaired = 10,
}

/// <summary>
/// Durable save-saga phase. It separates a safely cancellable candidate from an operation
/// after which the source world state may be irreversibly deleted.
/// </summary>
public enum PersistentInventorySavePhase
{
    None = 0,
    CandidateStaged = 1,
    WorldCleanupAuthorized = 2,
}

public enum PersistentInventoryMutationStatus
{
    Success = 0,
    Duplicate = 1,
    NotFound = 2,
    RevisionMismatch = 3,
    InvalidTransition = 4,
    StagingConflict = 5,
    CandidateNotFound = 6,
}

public readonly record struct PersistentInventoryAccountId(Guid Value)
{
    public override string ToString()
    {
        return Value.ToString();
    }
}

public readonly record struct PersistentInventorySnapshotId(Guid Value)
{
    public static PersistentInventorySnapshotId New()
    {
        return new PersistentInventorySnapshotId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}

public readonly record struct PersistentInventoryOperationId(Guid Value)
{
    public static PersistentInventoryOperationId New()
    {
        return new PersistentInventoryOperationId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}

public readonly record struct PersistentInventoryRevision(long Value)
{
    public static readonly PersistentInventoryRevision None = new(0);

    public override string ToString()
    {
        return Value.ToString();
    }
}

public readonly record struct PersistentInventoryLifeId(Guid Value)
{
    public static PersistentInventoryLifeId New()
    {
        return new PersistentInventoryLifeId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}

public readonly record struct PersistentInventoryServerEpoch(Guid Value)
{
    public static PersistentInventoryServerEpoch New()
    {
        return new PersistentInventoryServerEpoch(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}

/// <summary>
/// Metadata for one immutable revision without its binary payload.
/// </summary>
public sealed record PersistentInventoryRevisionMetadata(
    PersistentInventorySnapshotId SnapshotId,
    int SchemaVersion,
    string PolicyId,
    string? CapturedRoleId,
    string? CapturedProfileName,
    byte[] PayloadSha256,
    int ItemCount,
    int EntityCount,
    int UncompressedBytes,
    int CompressedBytes,
    PersistentInventoryOperationId OperationId,
    DateTime CreatedAt,
    DateTime SavedAt);

/// <summary>
/// Account header. Three references separate the verified, previous, and staging revisions.
/// </summary>
public sealed record PersistentInventorySnapshotHeader(
    PersistentInventoryAccountId AccountId,
    PersistentInventorySnapshotState State,
    PersistentInventorySnapshotState VerifiedState,
    PersistentInventorySavePhase SavePhase,
    PersistentInventoryRevision Revision,
    PersistentInventoryOperationId OperationId,
    PersistentInventoryRevisionMetadata? CurrentVerified,
    PersistentInventoryRevisionMetadata? LastKnownGood,
    PersistentInventoryRevisionMetadata? Staging,
    PersistentInventoryServerEpoch? ServerEpoch,
    PersistentInventoryServerEpoch? StagingServerEpoch,
    PersistentInventoryLifeId? LifeId,
    PersistentInventoryInvalidationReason InvalidationReason,
    PersistentInventoryLossReason LossReason,
    PersistentInventoryQuarantineReason QuarantineReason,
    string? ReasonDetails,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? RestoredAt,
    DateTime? InvalidatedAt,
    DateTime? LostAt,
    DateTime? WorldCleanupAuthorizedAt);

/// <summary>
/// Complete server-side revision. Client projects do not reference the assembly containing it.
/// </summary>
public sealed record PersistentInventoryStoredRevision(
    PersistentInventoryAccountId AccountId,
    PersistentInventoryRevisionMetadata Metadata,
    byte[] Payload);

public readonly record struct PersistentInventoryStateCount(
    PersistentInventorySnapshotState State,
    int Count);

public sealed record PersistentInventoryAuditRecord(
    long Id,
    PersistentInventoryAccountId AccountId,
    PersistentInventoryOperationId OperationId,
    PersistentInventoryAuditAction Action,
    PersistentInventorySnapshotState OldState,
    PersistentInventorySnapshotState NewState,
    PersistentInventoryRevision Revision,
    PersistentInventorySnapshotId? SnapshotId,
    Guid? ActorUserId,
    string Actor,
    string? Reason,
    int ItemCount,
    int EntityCount,
    int UncompressedBytes,
    int CompressedBytes,
    DateTime CreatedAt);

/// <summary>
/// Server-side candidate. During stage 1 the payload remains an opaque byte array unrelated to ECS.
/// </summary>
public sealed record PersistentInventoryStageRequest(
    PersistentInventorySnapshotId SnapshotId,
    PersistentInventoryOperationId OperationId,
    PersistentInventoryRevision ExpectedRevision,
    int SchemaVersion,
    string PolicyId,
    string? CapturedRoleId,
    string? CapturedProfileName,
    byte[] Payload,
    byte[] PayloadSha256,
    int ItemCount,
    int EntityCount,
    int UncompressedBytes,
    string Actor,
    Guid? ActorUserId = null,
    string? Reason = null,
    PersistentInventoryServerEpoch? ServerEpoch = null);

public sealed record PersistentInventoryAuthorizeWorldCleanupRequest(
    PersistentInventorySnapshotId SnapshotId,
    PersistentInventoryOperationId OperationId,
    PersistentInventoryRevision ExpectedRevision,
    PersistentInventoryServerEpoch ServerEpoch,
    string Actor,
    Guid? ActorUserId = null,
    string? Reason = null);

public sealed record PersistentInventoryPromoteRequest(
    PersistentInventorySnapshotId SnapshotId,
    PersistentInventoryOperationId OperationId,
    PersistentInventoryRevision ExpectedRevision,
    string Actor,
    Guid? ActorUserId = null,
    string? Reason = null);

/// <summary>
/// Atomically replaces the active immutable revision with a repaired copy
/// and removes the source payload containing obsolete entities.
/// </summary>
public sealed record PersistentInventoryRepairRequest(
    PersistentInventorySnapshotId SourceSnapshotId,
    PersistentInventorySnapshotId RepairedSnapshotId,
    PersistentInventoryOperationId OperationId,
    PersistentInventoryRevision ExpectedRevision,
    int SchemaVersion,
    string PolicyId,
    byte[] Payload,
    byte[] PayloadSha256,
    int ItemCount,
    int EntityCount,
    int UncompressedBytes,
    string Actor,
    Guid? ActorUserId = null,
    string? Reason = null);

public sealed record PersistentInventoryTransitionRequest(
    PersistentInventorySnapshotState NewState,
    PersistentInventoryOperationId OperationId,
    PersistentInventoryRevision ExpectedRevision,
    string Actor,
    Guid? ActorUserId = null,
    string? Reason = null,
    PersistentInventoryInvalidationReason InvalidationReason = PersistentInventoryInvalidationReason.None,
    PersistentInventoryLossReason LossReason = PersistentInventoryLossReason.None,
    PersistentInventoryQuarantineReason QuarantineReason = PersistentInventoryQuarantineReason.None,
    PersistentInventoryServerEpoch? ServerEpoch = null,
    PersistentInventoryLifeId? LifeId = null,
    PersistentInventoryAuditAction AuditAction = PersistentInventoryAuditAction.StateChanged);

public enum PersistentInventoryRevisionSelectionMode
{
    Rollback = 0,
    RecoverLost = 1,
    StartupFallback = 2,
}

/// <summary>
/// Switches the current verified reference to an existing server-side revision.
/// The method does not accept a payload and does not create a new item revision.
/// </summary>
public sealed record PersistentInventorySelectRevisionRequest(
    PersistentInventorySnapshotId SnapshotId,
    PersistentInventoryOperationId OperationId,
    PersistentInventoryRevision ExpectedRevision,
    PersistentInventoryRevisionSelectionMode Mode,
    string Actor,
    Guid? ActorUserId,
    string Reason);

public sealed record PersistentInventoryServerEpochRecord(
    PersistentInventoryServerEpoch ServerEpoch,
    DateTime StartedAt,
    DateTime? CleanShutdownAt);

public sealed record PersistentInventoryMutationResult(
    PersistentInventoryMutationStatus Status,
    PersistentInventorySnapshotHeader? Header,
    PersistentInventoryRevision AppliedRevision,
    PersistentInventorySnapshotState AppliedState,
    PersistentInventorySnapshotId? SnapshotId)
{
    public bool IsSuccess => Status is PersistentInventoryMutationStatus.Success
        or PersistentInventoryMutationStatus.Duplicate;
}

/// <summary>
/// Pure transition contract applied identically by the world and database layers.
/// </summary>
public static class PersistentInventoryStateMachine
{
    private static readonly IReadOnlySet<(PersistentInventorySnapshotState From, PersistentInventorySnapshotState To)>
        AllowedTransitions = new HashSet<(PersistentInventorySnapshotState, PersistentInventorySnapshotState)>
        {
            (PersistentInventorySnapshotState.None, PersistentInventorySnapshotState.Staging),
            (PersistentInventorySnapshotState.Staging, PersistentInventorySnapshotState.Active),
            (PersistentInventorySnapshotState.Staging, PersistentInventorySnapshotState.Aborted),
            (PersistentInventorySnapshotState.Staging, PersistentInventorySnapshotState.Quarantined),
            (PersistentInventorySnapshotState.Active, PersistentInventorySnapshotState.Staging),
            (PersistentInventorySnapshotState.Active, PersistentInventorySnapshotState.Bound),
            (PersistentInventorySnapshotState.Active, PersistentInventorySnapshotState.Invalid),
            (PersistentInventorySnapshotState.Active, PersistentInventorySnapshotState.Quarantined),
            (PersistentInventorySnapshotState.Bound, PersistentInventorySnapshotState.Staging),
            (PersistentInventorySnapshotState.Bound, PersistentInventorySnapshotState.Active),
            (PersistentInventorySnapshotState.Bound, PersistentInventorySnapshotState.Invalid),
            (PersistentInventorySnapshotState.Bound, PersistentInventorySnapshotState.LostByDisconnect),
            (PersistentInventorySnapshotState.Bound, PersistentInventorySnapshotState.Quarantined),
            (PersistentInventorySnapshotState.Invalid, PersistentInventorySnapshotState.Staging),
            (PersistentInventorySnapshotState.Invalid, PersistentInventorySnapshotState.Active),
            (PersistentInventorySnapshotState.LostByDisconnect, PersistentInventorySnapshotState.Staging),
            (PersistentInventorySnapshotState.LostByDisconnect, PersistentInventorySnapshotState.Active),
            (PersistentInventorySnapshotState.Aborted, PersistentInventorySnapshotState.Staging),
            (PersistentInventorySnapshotState.Quarantined, PersistentInventorySnapshotState.Staging),
            (PersistentInventorySnapshotState.Quarantined, PersistentInventorySnapshotState.Active),
        };

    public static bool CanTransition(
        PersistentInventorySnapshotState from,
        PersistentInventorySnapshotState to)
    {
        return AllowedTransitions.Contains((from, to));
    }

    public static bool HasValidTransitionMetadata(PersistentInventoryTransitionRequest request)
    {
        return request.NewState switch
        {
            PersistentInventorySnapshotState.Bound =>
                request.ServerEpoch != null && request.LifeId != null,
            PersistentInventorySnapshotState.Invalid =>
                request.InvalidationReason != PersistentInventoryInvalidationReason.None,
            PersistentInventorySnapshotState.LostByDisconnect =>
                request.LossReason != PersistentInventoryLossReason.None,
            PersistentInventorySnapshotState.Quarantined =>
                request.QuarantineReason != PersistentInventoryQuarantineReason.None,
            PersistentInventorySnapshotState.Active => true,
            PersistentInventorySnapshotState.Aborted => true,
            _ => false,
        };
    }
}
