using Content.Shared._WH40K.Administration.Mute;
using Robust.Shared.Localization;

namespace Content.Client._WH40K.Administration.Mute;

internal static class WH40KMuteDisplayHelper
{
    public static bool NeedsLiveRefresh(WH40KActiveMuteInfo? muteInfo)
    {
        return muteInfo?.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc - DateTime.UtcNow <= TimeSpan.FromHours(24);
    }

    public static string BuildChatPlaceholder(WH40KActiveMuteInfo? muteInfo)
    {
        return muteInfo?.ExpiresAtUtc is { } expiresAtUtc
            ? BuildTemporaryPlaceholder(expiresAtUtc, "wh40k-chat-mute-placeholder-duration", "wh40k-chat-mute-placeholder-until")
            : Loc.GetString("wh40k-chat-mute-placeholder-permanent");
    }

    public static string BuildAHelpPlaceholder(WH40KActiveMuteInfo? muteInfo)
    {
        return muteInfo?.ExpiresAtUtc is { } expiresAtUtc
            ? BuildTemporaryPlaceholder(expiresAtUtc, "wh40k-ahelp-mute-placeholder-duration", "wh40k-ahelp-mute-placeholder-until")
            : Loc.GetString("wh40k-ahelp-mute-placeholder-permanent");
    }

    public static string BuildMuteTooltip(WH40KActiveMuteInfo? muteInfo)
    {
        return muteInfo?.ExpiresAtUtc is { } expiresAtUtc
            ? Loc.GetString("wh40k-mute-tooltip-temporary", ("reason", muteInfo.Reason), ("time", FormatAbsoluteDate(expiresAtUtc)))
            : Loc.GetString("wh40k-mute-tooltip-permanent", ("reason", muteInfo?.Reason ?? string.Empty));
    }

    private static string BuildTemporaryPlaceholder(DateTime expiresAtUtc, string durationKey, string untilKey)
    {
        return expiresAtUtc - DateTime.UtcNow > TimeSpan.FromHours(24)
            ? Loc.GetString(untilKey, ("time", FormatAbsoluteDate(expiresAtUtc)))
            : Loc.GetString(durationKey, ("time", FormatRelativeDuration(expiresAtUtc)));
    }

    private static string FormatRelativeDuration(DateTime expiresAtUtc)
    {
        var remaining = expiresAtUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return Loc.GetString("wh40k-mute-time-seconds", ("count", 0));
        if (remaining.TotalMinutes < 1)
            return Loc.GetString("wh40k-mute-time-seconds", ("count", Math.Max(1, (int) Math.Floor(remaining.TotalSeconds))));
        if (remaining.TotalHours < 1)
            return Loc.GetString("wh40k-mute-time-minutes", ("count", Math.Max(1, (int) Math.Ceiling(remaining.TotalMinutes))));

        var hours = Math.Max(1, (int) remaining.TotalHours);
        var minutes = (int) Math.Ceiling(Math.Max(0, (remaining - TimeSpan.FromHours(hours)).TotalMinutes));
        if (minutes >= 60)
        {
            hours += minutes / 60;
            minutes %= 60;
        }

        return minutes <= 0
            ? Loc.GetString("wh40k-mute-time-hours", ("count", hours))
            : Loc.GetString("wh40k-mute-time-hours-minutes", ("hours", hours), ("minutes", minutes));
    }

    private static string FormatAbsoluteDate(DateTime expiresAtUtc)
    {
        return expiresAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }
}
