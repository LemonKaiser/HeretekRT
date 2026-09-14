using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared._WH40K.DirectionalEmote;
using Content.Shared.ActionBlocker;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Examine;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.DirectionalEmote;

public sealed partial class WH40KDirectionalEmoteSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private int _maxEmoteLength = 256;
    private float _maxEmoteDistance = 2.3f;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.DirectionalEmoteMaxLength, value => _maxEmoteLength = value, true);
        Subs.CVar(_cfg, CCVars.DirectionalEmoteMaxDistance, value => _maxEmoteDistance = value, true);
        SubscribeNetworkEvent<WH40KDirectionalEmoteAttemptEvent>(OnAttempt);
    }

    private void OnAttempt(WH40KDirectionalEmoteAttemptEvent args, EntitySessionEventArgs eventArgs)
    {
        if (eventArgs.SenderSession.AttachedEntity is not { } source)
            return;

        // Directed emotes are still emotes: honor the same authoritative restrictions as
        // ordinary emotes, including persistent chat mutes and status-effect blockers.
        if (!_actionBlocker.CanEmote(source))
            return;

        if (!TryGetEntity(args.Target, out var target) || target is not { } targetUid)
            return;

        if (!IsValid(args, source, targetUid))
            return;

        if (!TryComp<ActorComponent>(source, out var sourceActor) ||
            !TryComp<ActorComponent>(targetUid, out var targetActor) ||
            !TryComp<WH40KDirectionalEmoteComponent>(source, out var sourceEmote))
        {
            return;
        }

        var escapedText = FormattedMessage.EscapeText(args.Text);
        var targetName = FormattedMessage.EscapeText(MetaData(targetUid).EntityName);
        var wrappedMessage = Loc.GetString(
            args.HideName
                ? "wh40k-directional-emote-hidden-wrap-message"
                : "wh40k-directional-emote-wrap-message",
            ("source", FormattedMessage.EscapeText(MetaData(source).EntityName)),
            ("target", targetName),
            ("message", escapedText));

        _chatManager.ChatMessageToMany(
            ChatChannel.Emotes,
            args.Text,
            wrappedMessage,
            source,
            false,
            true,
            [sourceActor.PlayerSession.Channel, targetActor.PlayerSession.Channel]);
        _adminLog.Add(LogType.Chat, LogImpact.Low,
            $"{ToPrettyString(source):source} sent directed emote to {ToPrettyString(targetUid):target}: {args.Text}");

        sourceEmote.LastSendAt = _timing.CurTime;
        sourceEmote.LastEmote = args.Text;
        Dirty(source, sourceEmote);
    }

    private bool IsValid(WH40KDirectionalEmoteAttemptEvent args, EntityUid source, EntityUid target)
    {
        if (source == target ||
            !TryComp<WH40KDirectionalEmoteComponent>(source, out var sourceEmote) ||
            !TryComp<WH40KDirectionalEmoteComponent>(target, out var targetEmote))
        {
            return false;
        }

        if (!sourceEmote.CanSendEmotes || !targetEmote.CanReceiveEmotes ||
            args.HideName && !sourceEmote.CanHideName ||
            sourceEmote.LastSendAt + sourceEmote.Cooldown > _timing.CurTime ||
            args.Text.Length > _maxEmoteLength || string.IsNullOrWhiteSpace(args.Text))
        {
            return false;
        }

        return _examine.InRangeUnOccluded(source, target, _maxEmoteDistance);
    }
}
