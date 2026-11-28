using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ShipPa;

/// <summary>
/// Replicated ship Vox-PA timeline on a grid; clients render at most two local voices from the nearest working speaker.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kShipPaBroadcastComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<Wh40kShipPaBroadcast> Broadcasts = new();
}

[Serializable, NetSerializable]
public enum Wh40kShipPaBroadcastKind : byte
{
    Announcement,
    Alarm,
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class Wh40kShipPaBroadcast
{
    [DataField] public int Id;
    [DataField] public string Key = string.Empty;
    [DataField] public string Path = string.Empty;
    [DataField] public TimeSpan Start;
    [DataField] public float Length;
    [DataField] public bool Loop;
    [DataField] public AudioParams Params = AudioParams.Default;
    [DataField] public int Priority;
    [DataField] public Wh40kShipPaBroadcastKind Kind;
    [DataField] public string? Caption;
    [DataField] public Color Color = Color.White;
    [DataField] public TimeSpan RetainUntil;

    public bool IsActive(TimeSpan now) => now < Start || IsPlaying(now);

    public bool IsPlaying(TimeSpan now) => now >= Start && (Loop || now < Start + TimeSpan.FromSeconds(Length));

    public float Position(TimeSpan now)
    {
        var elapsed = Math.Max(0f, (float) (now - Start).TotalSeconds);
        return Loop && Length > 0f ? elapsed % Length : Math.Min(elapsed, Length);
    }
}

public static class Wh40kShipPaPlaybackPolicy
{
    public const int MaxSources = 2;
    public const int DefaultAlarmPriority = 20;
    public const int AnnouncementPriority = 30;
    public const float StartLeadSeconds = 0.5f;
    public const float FadeSeconds = 0.25f;

    public static bool ShouldSwitch(float currentScore, float candidateScore) => candidateScore < currentScore * 0.8f;

    public static float Score(float distance, float range, float occlusion) =>
        distance / Math.Max(1f, range) + Math.Max(0f, occlusion) * 0.25f;
}
