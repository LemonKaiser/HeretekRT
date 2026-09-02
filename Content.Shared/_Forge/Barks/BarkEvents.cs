using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Barks;

[Serializable, NetSerializable]
public sealed class RequestPreviewBarkEvent(string barkVoiceId) : EntityEventArgs
{
    public string BarkVoiceId { get; } = barkVoiceId;
}

[Serializable, NetSerializable]
public sealed class PlayBarkEvent(
    string soundPath,
    NetEntity sourceUid,
    string message,
    float playbackSpeed,
    bool isWhisper) : EntityEventArgs
{
    public string SoundPath { get; } = soundPath;
    public NetEntity SourceUid { get; } = sourceUid;
    public string Message { get; } = message;
    public float PlaybackSpeed { get; } = playbackSpeed;
    public bool IsWhisper { get; } = isWhisper;
}

[Serializable, NetSerializable]
public sealed class PlayBarkPreviewEvent(string soundPath) : EntityEventArgs
{
    public string SoundPath { get; } = soundPath;
}
