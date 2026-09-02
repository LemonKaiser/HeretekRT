using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    /// Automatic consequence for a standard chat-rate-limit violation. Supported values: none, mute.
    /// </summary>
    public static readonly CVarDef<string> ChatRateLimitPunishment =
        CVarDef.Create("chat.rate_limit_punishment", "none", CVar.SERVERONLY);

    /// <summary>Temporary mute duration, in minutes, for a standard rate-limit violation.</summary>
    public static readonly CVarDef<int> ChatRateLimitMuteMinutes =
        CVarDef.Create("chat.rate_limit_mute_minutes", 1, CVar.SERVERONLY);

    /// <summary>Whether to erase visible chat lines after a standard rate-limit violation.</summary>
    public static readonly CVarDef<bool> ChatRateLimitDeleteMessages =
        CVarDef.Create("chat.rate_limit_delete_messages", false, CVar.SERVERONLY);

    /// <summary>Window in seconds for repeated identical normalized chat messages.</summary>
    public static readonly CVarDef<float> ChatRepeatRateLimitPeriod =
        CVarDef.Create("chat.repeat_rate_limit_period", 5f, CVar.SERVERONLY);

    /// <summary>Repeated-message count that blocks the matching message. Set to zero to disable.</summary>
    public static readonly CVarDef<int> ChatRepeatRateLimitCount =
        CVarDef.Create("chat.repeat_rate_limit_count", 3, CVar.SERVERONLY);

    /// <summary>Minimum interval in seconds between repeat-spam alerts. Negative disables alerts.</summary>
    public static readonly CVarDef<int> ChatRepeatRateLimitAnnounceAdminsDelay =
        CVarDef.Create("chat.repeat_rate_limit_announce_admins_delay", 30, CVar.SERVERONLY);

    /// <summary>Automatic consequence for repeated-message spam. Supported values: none, mute.</summary>
    public static readonly CVarDef<string> ChatRepeatRateLimitPunishment =
        CVarDef.Create("chat.repeat_rate_limit_punishment", "none", CVar.SERVERONLY);

    /// <summary>Temporary mute duration, in minutes, for repeated-message spam.</summary>
    public static readonly CVarDef<int> ChatRepeatRateLimitMuteMinutes =
        CVarDef.Create("chat.repeat_rate_limit_mute_minutes", 1, CVar.SERVERONLY);

    /// <summary>Whether to erase visible chat lines after repeated-message spam.</summary>
    public static readonly CVarDef<bool> ChatRepeatRateLimitDeleteMessages =
        CVarDef.Create("chat.repeat_rate_limit_delete_messages", false, CVar.SERVERONLY);
}
