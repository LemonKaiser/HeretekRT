using Robust.Shared.Configuration;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Factions.Capture;

[CVarDefs]
public static class FactionCaptureCVars
{
    public static readonly CVarDef<int> Interval = CVarDef.Create("wh40k.capture_interval_seconds", 7200, CVar.SERVERONLY);
    public static readonly CVarDef<int> Window = CVarDef.Create("wh40k.capture_window_seconds", 3600, CVar.SERVERONLY);
    public static readonly CVarDef<int> Hold = CVarDef.Create("wh40k.capture_hold_seconds", 60, CVar.SERVERONLY);
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactionCapturePointComponent : Component
{
    public static readonly Color NeutralColor = Color.FromHex("#AAB1BA");

    [DataField, AutoNetworkedField]
    public float Radius = 9f;

    [DataField]
    public string? SiteName;

    [AutoNetworkedField]
    public FactionCaptureStatus Status;

    [AutoNetworkedField]
    public Color BoundaryColor = NeutralColor;

    [AutoNetworkedField]
    public float CaptureProgress;

    // Client-side interpolation between authoritative capture samples.
    public float DisplayedProgress;

    public bool Active => Status is FactionCaptureStatus.Available or FactionCaptureStatus.Capturing or
        FactionCaptureStatus.Contested or FactionCaptureStatus.Held;
}

[RegisterComponent]
public sealed partial class FactionCaptureTerminalComponent : Component;

[Serializable, NetSerializable]
public enum FactionCaptureUiKey : byte { Key }

[Serializable, NetSerializable]
public enum FactionCaptureStatus : byte { Cooldown, Available, Capturing, Contested, Held, Offline }

[Serializable, NetSerializable]
public sealed record FactionCaptureSite(int Number, string? Name, FactionCaptureStatus Status, string? Owner,
    string? Challenger, int CaptureSeconds, int Reward);

[Serializable, NetSerializable]
public sealed class FactionCaptureUiState(FactionCaptureSite[] sites, int nextActivationSeconds,
    int windowSeconds, int holdSeconds) : BoundUserInterfaceState
{
    public FactionCaptureSite[] Sites { get; } = sites;
    public int NextActivationSeconds { get; } = nextActivationSeconds;
    public int WindowSeconds { get; } = windowSeconds;
    public int HoldSeconds { get; } = holdSeconds;
}
