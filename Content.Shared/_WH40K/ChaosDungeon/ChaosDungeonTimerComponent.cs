using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.ChaosDungeon;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ChaosDungeonTimerComponent : Component
{
    [AutoNetworkedField]
    public TimeSpan ExpiresAt;

    [AutoNetworkedField]
    public bool Extraction;
}

public static class ChaosDungeonTiming
{
    public static string FormatCountdown(TimeSpan remaining)
    {
        var seconds = Math.Max(0, (int) Math.Ceiling(remaining.TotalSeconds));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }
}
