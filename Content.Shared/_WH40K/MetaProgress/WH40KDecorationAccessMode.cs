namespace Content.Shared._WH40K.MetaProgress;

public enum WH40KDecorationAccessMode : byte
{
    Disabled,
    Admins,
    All,
}

/// <summary>
///     Parses the shared decorations access CVar consistently on the client and server.
/// </summary>
public static class WH40KDecorationAccessPolicy
{
    public static WH40KDecorationAccessMode ParseMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "admin" => WH40KDecorationAccessMode.Admins,
            "all" => WH40KDecorationAccessMode.All,
            _ => WH40KDecorationAccessMode.Disabled,
        };
    }
}
