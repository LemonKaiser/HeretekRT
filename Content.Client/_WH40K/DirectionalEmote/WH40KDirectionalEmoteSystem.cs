using Content.Client._WH40K.DirectionalEmote.UserInterface;
using Content.Client.Examine;
using Content.Client.Popups;
using Content.Shared._WH40K.DirectionalEmote;
using Content.Shared.CCVar;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.DirectionalEmote;

public sealed partial class WH40KDirectionalEmoteSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _userInterfaceManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ExamineSystem _examine = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    private int _maxEmoteLength = 256;
    private float _maxEmoteDistance = 2.3f;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.DirectionalEmoteMaxLength, value => _maxEmoteLength = value, true);
        Subs.CVar(_cfg, CCVars.DirectionalEmoteMaxDistance, value => _maxEmoteDistance = value, true);
        SubscribeLocalEvent<WH40KDirectionalEmoteComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    public void TrySendEmote(NetEntity source, NetEntity target, string message, bool hideName = false)
    {
        var sourceEntity = GetEntity(source);
        var targetEntity = GetEntity(target);

        if (sourceEntity == targetEntity ||
            !TryComp<WH40KDirectionalEmoteComponent>(sourceEntity, out var sourceEmote) ||
            sourceEmote.LastSendAt + sourceEmote.Cooldown > _gameTiming.CurTime)
        {
            return;
        }

        if (message.Length > _maxEmoteLength || string.IsNullOrWhiteSpace(message))
        {
            _popupSystem.PopupCursor(
                Loc.GetString("wh40k-directional-emote-length-error", ("limit", _maxEmoteLength)),
                PopupType.MediumCaution);
            return;
        }

        if (!_examine.InRangeUnOccluded(sourceEntity, targetEntity, _maxEmoteDistance))
        {
            _popupSystem.PopupCursor(
                Loc.GetString("wh40k-directional-emote-too-far", ("range", Math.Round(_maxEmoteDistance, 1))),
                PopupType.MediumCaution);
            return;
        }

        sourceEmote.LastSendAt = _gameTiming.CurTime;
        RaiseNetworkEvent(new WH40KDirectionalEmoteAttemptEvent(target, message, hideName));
    }

    private void OnGetVerbs(EntityUid uid, WH40KDirectionalEmoteComponent component, GetVerbsEvent<Verb> args)
    {
        if (args.User == uid ||
            !_examine.InRangeUnOccluded(args.User, args.Target, _maxEmoteDistance) ||
            !component.CanReceiveEmotes ||
            !TryComp<WH40KDirectionalEmoteComponent>(args.User, out var userEmote) ||
            !userEmote.CanSendEmotes)
        {
            return;
        }

        var uiController = _userInterfaceManager.GetUIController<WH40KDirectionalEmoteUIController>();
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wh40k-directional-emote-verb"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/emotes.svg.192dpi.png")),
            Act = () => uiController.OpenWindow(GetNetEntity(args.User), GetNetEntity(args.Target)),
        });
    }
}
