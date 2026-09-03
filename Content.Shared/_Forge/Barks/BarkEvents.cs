using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Barks;

[Serializable, NetSerializable]
public sealed class RequestPreviewBarkEvent(string barkVoiceId) : EntityEventArgs
{
    public string BarkVoiceId { get; } = barkVoiceId;
}

[Serializable, NetSerializable]
public readonly record struct BarkSoundEventData(
    float Delay,
    int SoundIndex,
    float PitchScale,
    float VolumeOffset);

[Serializable, NetSerializable]
public sealed class PlayBarkEvent(
    string[] soundPaths,
    NetEntity sourceUid,
    BarkSoundEventData[] barks,
    bool isWhisper,
    float playbackSpeed) : EntityEventArgs
{
    public string[] SoundPaths { get; } = soundPaths;
    public NetEntity SourceUid { get; } = sourceUid;
    public BarkSoundEventData[] Barks { get; } = barks;
    public bool IsWhisper { get; } = isWhisper;
    public float PlaybackSpeed { get; } = playbackSpeed;
}

[Serializable, NetSerializable]
public sealed class PlayBarkPreviewEvent(string soundPath) : EntityEventArgs
{
    public string SoundPath { get; } = soundPath;
}
