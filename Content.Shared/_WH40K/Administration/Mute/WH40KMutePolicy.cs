using System;
using System.Collections.Generic;
using System.Text;

namespace Content.Shared._WH40K.Administration.Mute;

/// <summary>
/// Shared limits and normalization rules for persistent administrative mutes.
/// Keeping these rules shared prevents the command, EUI and server authority from drifting apart.
/// </summary>
public static class WH40KMutePolicy
{
    public const int MaxReasonLength = 4096;
    public static readonly WH40KMuteType AllScopes = WH40KMuteType.Chat | WH40KMuteType.AHelp;
    public static readonly TimeSpan MaxTemporaryDuration = TimeSpan.FromDays(3650);

    public static bool IsValidScopeMask(WH40KMuteType typeMask)
    {
        return typeMask != WH40KMuteType.None && (typeMask & ~AllScopes) == WH40KMuteType.None;
    }

    public static WH40KMuteType NormalizeRemovalScopeMask(WH40KMuteType typeMask)
    {
        return typeMask == WH40KMuteType.None ? AllScopes : typeMask;
    }

    public static bool IsValidTemporaryDuration(TimeSpan? duration)
    {
        return duration == null || duration > TimeSpan.Zero && duration <= MaxTemporaryDuration;
    }

    public static bool TryNormalizeReason(string? rawReason, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(rawReason))
            return false;

        var builder = new StringBuilder(Math.Min(rawReason.Length, MaxReasonLength));
        var previousWasWhitespace = false;
        foreach (var character in rawReason.Trim())
        {
            if (char.IsControl(character))
            {
                previousWasWhitespace = builder.Length > 0;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                previousWasWhitespace = builder.Length > 0;
                continue;
            }

            if (previousWasWhitespace)
            {
                builder.Append(' ');
                previousWasWhitespace = false;
            }

            builder.Append(character);
            if (builder.Length == MaxReasonLength)
                break;
        }

        reason = builder.ToString();
        return !string.IsNullOrWhiteSpace(reason);
    }

    public static IReadOnlyCollection<WH40KMuteType> EnumerateScopes(WH40KMuteType typeMask)
    {
        if (!IsValidScopeMask(typeMask))
            throw new ArgumentOutOfRangeException(nameof(typeMask));

        return typeMask switch
        {
            WH40KMuteType.Chat => [WH40KMuteType.Chat],
            WH40KMuteType.AHelp => [WH40KMuteType.AHelp],
            WH40KMuteType.Chat | WH40KMuteType.AHelp => [WH40KMuteType.Chat, WH40KMuteType.AHelp],
            _ => throw new ArgumentOutOfRangeException(nameof(typeMask)),
        };
    }
}
