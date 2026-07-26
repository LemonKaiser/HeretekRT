namespace Content.Shared._WH40K.CharacterCreation;

/// <summary>
/// Result of the server-authoritative first-character replacement.
/// </summary>
public enum Wh40kOnboardingCompletionStatus : byte
{
    Success,
    NotAllowed,
    InvalidProfile,
    InvalidBuild,
    PersistenceFailed,
}

public readonly record struct Wh40kOnboardingCompletionResult(
    Wh40kOnboardingCompletionStatus Status,
    Wh40kPlayerProgressSnapshot Progress,
    int ProfileSlot)
{
    public bool IsSuccess => Status == Wh40kOnboardingCompletionStatus.Success;
}
