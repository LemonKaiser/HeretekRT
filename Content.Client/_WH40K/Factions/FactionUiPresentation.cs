using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Factions;
using Content.Shared._WH40K.Progression;
using Robust.Client.Graphics;

namespace Content.Client._WH40K.Factions;

internal static class FactionUiPresentation
{
    public static readonly Color Text = Color.FromHex("#DDDCD3");
    public static readonly Color Muted = Color.FromHex("#9CA69F");
    public static readonly Color Gold = Color.FromHex("#C4AC78");

    public static StyleBoxFlat Panel(Color accent, bool selected = false) => new()
    {
        BackgroundColor = Color.FromHex(selected ? "#29342D" : "#202622"),
        BorderColor = selected ? accent : Color.FromHex("#48534A"),
        BorderThickness = new Thickness(selected ? 3 : 1, 1, 1, 1),
        ContentMarginLeftOverride = 12,
        ContentMarginRightOverride = 12,
        ContentMarginTopOverride = 10,
        ContentMarginBottomOverride = 10,
    };

    public static string Role(FactionRole role, FactionProfile? profile = null) =>
        profile?.RankTitle(role) is { Length: > 0 } title ? title : Loc.GetString(role switch
    {
        FactionRole.Leader => "wh40k-faction-role-leader",
        FactionRole.Officer => "wh40k-faction-role-officer",
        _ => "wh40k-faction-role-member",
    });

    public static string Ranks(FactionProfile profile) => Loc.GetString("wh40k-faction-custom-ranks-help",
        ("leader", Role(FactionRole.Leader, profile)), ("officer", Role(FactionRole.Officer, profile)),
        ("member", Role(FactionRole.Member, profile)));

    public static string Summary(CompanyPrototype faction) =>
        faction.Summary is { } key ? Loc.GetString(key) : Loc.GetString("wh40k-faction-summary-fallback");

    public static string ProfileAction(string action) => action == Loc.GetString("wh40k-faction-join")
        ? Loc.GetString("wh40k-faction-submit-application") : action;

    public static string Overview(Wh40kFactionOptionUiSnapshot? faction, bool detailed = false)
    {
        if (faction == null)
            return Loc.GetString("wh40k-faction-loading");
        var leader = string.IsNullOrWhiteSpace(faction.LeaderName)
            ? Loc.GetString("wh40k-faction-no-leader")
            : faction.LeaderName;
        return Loc.GetString(detailed ? "wh40k-faction-overview-profile" : "wh40k-faction-overview-card",
            ("leader", leader), ("members", faction.MemberCount), ("officers", faction.OfficerCount));
    }

    public static string Status(Wh40kFactionUiOperationStatus status)
    {
        var key = status switch
        {
            Wh40kFactionUiOperationStatus.None => null,
            Wh40kFactionUiOperationStatus.Success => "wh40k-faction-status-success",
            Wh40kFactionUiOperationStatus.AccountUnavailable => "wh40k-faction-status-account-unavailable",
            Wh40kFactionUiOperationStatus.InvalidFaction => "wh40k-faction-status-invalid-faction",
            Wh40kFactionUiOperationStatus.AlreadyMember => "wh40k-faction-status-already-member",
            Wh40kFactionUiOperationStatus.AlreadyRequested => "wh40k-faction-status-already-requested",
            Wh40kFactionUiOperationStatus.RequestNotFound => "wh40k-faction-status-request-not-found",
            Wh40kFactionUiOperationStatus.NotAuthorized => "wh40k-faction-status-not-authorized",
            Wh40kFactionUiOperationStatus.CannotManageLeader => "wh40k-faction-status-cannot-manage-leader",
            Wh40kFactionUiOperationStatus.RequestSubmitted => "wh40k-faction-status-submitted",
            Wh40kFactionUiOperationStatus.RequestCancelled => "wh40k-faction-status-cancelled",
            Wh40kFactionUiOperationStatus.RequestAccepted => "wh40k-faction-status-accepted",
            Wh40kFactionUiOperationStatus.RequestRejected => "wh40k-faction-status-rejected",
            Wh40kFactionUiOperationStatus.MemberRemoved => "wh40k-faction-status-removed",
            Wh40kFactionUiOperationStatus.OfficerPromoted => "wh40k-faction-status-promoted",
            Wh40kFactionUiOperationStatus.OfficerDemoted => "wh40k-faction-status-demoted",
            Wh40kFactionUiOperationStatus.LeftFaction => "wh40k-faction-status-left",
            Wh40kFactionUiOperationStatus.ProfileSaved => "wh40k-faction-profile-saved",
            Wh40kFactionUiOperationStatus.InvalidProfile => "wh40k-faction-profile-invalid",
            Wh40kFactionUiOperationStatus.ProfileConflict => "wh40k-faction-profile-conflict",
            Wh40kFactionUiOperationStatus.InvalidPayout => "wh40k-faction-bank-invalid-payout",
            Wh40kFactionUiOperationStatus.BankUnavailable => "wh40k-faction-bank-unavailable",
            Wh40kFactionUiOperationStatus.InsufficientFactionFunds => "wh40k-faction-bank-insufficient",
            Wh40kFactionUiOperationStatus.RecipientUnavailable => "wh40k-faction-bank-recipient-unavailable",
            Wh40kFactionUiOperationStatus.RecipientBalanceLimit => "wh40k-faction-bank-recipient-limit",
            Wh40kFactionUiOperationStatus.PayoutSent => "wh40k-faction-bank-sent",
            Wh40kFactionUiOperationStatus.PayoutQueued => "wh40k-faction-bank-queued",
            Wh40kFactionUiOperationStatus.InvalidSocialText => "wh40k-faction-comms-invalid",
            Wh40kFactionUiOperationStatus.SocialCooldown => "wh40k-faction-comms-cooldown",
            Wh40kFactionUiOperationStatus.TransmissionUnavailable => "wh40k-faction-comms-offline",
            Wh40kFactionUiOperationStatus.MessageSent => "wh40k-faction-comms-sent",
            Wh40kFactionUiOperationStatus.DistressSent => "wh40k-faction-comms-distress-sent",
            Wh40kFactionUiOperationStatus.AnnouncementPublished => "wh40k-faction-comms-published",
            Wh40kFactionUiOperationStatus.AnnouncementRemoved => "wh40k-faction-comms-removed",
            Wh40kFactionUiOperationStatus.InvalidRelation => "wh40k-faction-rel-invalid",
            Wh40kFactionUiOperationStatus.RelationConflict => "wh40k-faction-rel-conflict",
            Wh40kFactionUiOperationStatus.RelationSaved => "wh40k-faction-rel-saved",
            Wh40kFactionUiOperationStatus.RelationCooldown => "wh40k-faction-rel-cooldown-error",
            _ => "wh40k-faction-status-failed",
        };
        return key == null ? string.Empty : Loc.GetString(key);
    }
}
