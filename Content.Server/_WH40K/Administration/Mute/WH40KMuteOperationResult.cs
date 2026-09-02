namespace Content.Server._WH40K.Administration.Mute;

public enum WH40KMuteApplyResult : byte
{
    Applied,
    InvalidScope,
    InvalidReason,
    InvalidDuration,
    TargetHostProtected,
}

public readonly record struct WH40KMuteRemovalResult(bool Allowed, int RemovedCount);
