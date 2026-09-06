using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._WH40K.Administration;
using Content.Server._WH40K.Administration.Mute;
using Content.Server._WH40K.Chat.Moderation;
using Content.Server.Chat.V2.Repository;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Players.RateLimiting;
using Content.Shared._WH40K.Administration.Mute;
using Robust.Shared.Player;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server.Chat.Managers;

internal sealed partial class ChatManager
{
    private readonly Dictionary<NetUserId, WH40KRepeatedChatSpamTracker> _repeatRateLimitData = new();

    [Dependency] private IGameTiming _gameTiming = default!;

    private TimeSpan _nextRepeatRateLimitSweep;

    public RateLimitStatus HandleRepeatedRateLimit(ICommonSession player, string message)
    {
        if (ShouldBypassWh40KChatRateLimit(player))
            return RateLimitStatus.Allowed;

        var threshold = _configurationManager.GetCVar(CCVars.ChatRepeatRateLimitCount);
        var periodSeconds = _configurationManager.GetCVar(CCVars.ChatRepeatRateLimitPeriod);
        if (threshold <= 0 || periodSeconds <= 0)
            return RateLimitStatus.Allowed;

        var normalized = WH40KRepeatedChatSpamTracker.NormalizeMessage(message);
        if (normalized.Length == 0)
            return RateLimitStatus.Allowed;

        var now = _gameTiming.RealTime;
        SweepRepeatRateLimitState(now);
        if (!_repeatRateLimitData.TryGetValue(player.UserId, out var tracker))
        {
            tracker = new WH40KRepeatedChatSpamTracker();
            _repeatRateLimitData.Add(player.UserId, tracker);
        }

        var announceDelaySeconds = _configurationManager.GetCVar(CCVars.ChatRepeatRateLimitAnnounceAdminsDelay);
        var result = tracker.CountMessage(
            now,
            normalized,
            TimeSpan.FromSeconds(periodSeconds),
            threshold,
            announceDelaySeconds < 0 ? null : TimeSpan.FromSeconds(announceDelaySeconds));
        if (!result.Blocked)
            return RateLimitStatus.Allowed;

        if (result.ShouldAnnounceAdmins)
        {
            SendAdminAlert(Loc.GetString(
                "chat-manager-repeat-rate-limit-admin-announcement",
                ("player", player.Name),
                ("message", TruncateForModerationLog(message))));
        }

        if (result.FirstViolation)
        {
            DispatchServerMessage(player, Loc.GetString("chat-manager-repeat-rate-limited"), suppressLog: true);
            _adminLogger.Add(
                LogType.ChatRateLimited,
                LogImpact.Medium,
                $"Player {player} breached repeated chat spam limit with message '{TruncateForModerationLog(message)}'");
            HandleAutomaticSpamConsequence(player, WH40KChatSpamTrigger.RepeatRateLimit);
        }

        return RateLimitStatus.Blocked;
    }

    private void SweepRepeatRateLimitState(TimeSpan now)
    {
        if (_nextRepeatRateLimitSweep > now)
            return;

        _nextRepeatRateLimitSweep = now + TimeSpan.FromSeconds(30);
        List<NetUserId>? expiredUsers = null;
        foreach (var (userId, tracker) in _repeatRateLimitData)
        {
            if (!tracker.CleanupExpired(now))
                continue;

            expiredUsers ??= new List<NetUserId>();
            expiredUsers.Add(userId);
        }

        if (expiredUsers == null)
            return;

        foreach (var userId in expiredUsers)
        {
            _repeatRateLimitData.Remove(userId);
        }
    }

    private bool ShouldBypassWh40KChatRateLimit(ICommonSession player)
    {
        return WH40KStaffProtection.ShouldBypassChatRateLimits(
            _adminManager.GetAdminData(player),
            _adminManager.GetAdminData(player, includeDeAdmin: true),
            _adminManager.IsPromotedHost(player.UserId));
    }

    private void HandleAutomaticSpamConsequence(ICommonSession player, WH40KChatSpamTrigger trigger)
    {
        var deleteMessages = _configurationManager.GetCVar(trigger == WH40KChatSpamTrigger.RateLimit
            ? CCVars.ChatRateLimitDeleteMessages
            : CCVars.ChatRepeatRateLimitDeleteMessages);
        if (deleteMessages)
            DeletePlayerMessages(player.UserId);

        var punishment = _configurationManager.GetCVar(trigger == WH40KChatSpamTrigger.RateLimit
            ? CCVars.ChatRateLimitPunishment
            : CCVars.ChatRepeatRateLimitPunishment);
        if (!string.Equals(punishment.Trim(), "mute", StringComparison.OrdinalIgnoreCase))
            return;

        var configuredMinutes = _configurationManager.GetCVar(trigger == WH40KChatSpamTrigger.RateLimit
            ? CCVars.ChatRateLimitMuteMinutes
            : CCVars.ChatRepeatRateLimitMuteMinutes);
        var minutes = Math.Clamp(configuredMinutes, 1, (int) WH40KMutePolicy.MaxTemporaryDuration.TotalMinutes);
        _ = ApplyAutomaticChatMuteAsync(player, trigger, minutes);
    }

    private void DeletePlayerMessages(NetUserId userId)
    {
        DeleteMessagesBy(userId);
        _entityManager.System<ChatRepositorySystem>().NukeForUserId(userId, out _);
    }

    private async Task ApplyAutomaticChatMuteAsync(
        ICommonSession player,
        WH40KChatSpamTrigger trigger,
        int muteMinutes)
    {
        try
        {
            var mutes = _entityManager.System<WH40KMuteSystem>();
            var result = await mutes.ApplyMuteAsync(
                player.UserId,
                player.Name,
                WH40KMuteType.Chat,
                Loc.GetString(trigger == WH40KChatSpamTrigger.RateLimit
                    ? "chat-manager-rate-limit-auto-mute-reason"
                    : "chat-manager-repeat-rate-limit-auto-mute-reason"),
                TimeSpan.FromMinutes(muteMinutes),
                adminUserId: null,
                eraseMessages: false);
            if (result != WH40KMuteApplyResult.Applied || !mutes.IsChatMuted(player, out _))
                return;

            DispatchServerMessage(player, Loc.GetString(
                trigger == WH40KChatSpamTrigger.RateLimit
                    ? "chat-manager-rate-limit-auto-muted"
                    : "chat-manager-repeat-rate-limit-auto-muted",
                ("minutes", muteMinutes)), suppressLog: true);
        }
        catch (Exception e)
        {
            Logger.GetSawmill("chat.moderation").Error($"Failed to apply automatic chat mute to {player}: {e}");
        }
    }

    private static string TruncateForModerationLog(string value)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ');
        const int maxLength = 200;
        return normalized.Length <= maxLength ? normalized : $"{normalized[..maxLength]}...";
    }

    private enum WH40KChatSpamTrigger : byte
    {
        RateLimit,
        RepeatRateLimit,
    }
}
