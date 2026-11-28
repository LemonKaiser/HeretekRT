using Content.Server.Chat.Managers;
using Content.Server.PDA;
using Content.Shared._WH40K.Cyborgs;
using Content.Shared.CartridgeLoader;
using Content.Shared.Chat;
using Content.Shared.PDA;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.Cyborgs;

/// <summary>
/// Names a cyborg's built-in PDA after the cyborg and forwards cartridge notifications to its chat.
/// </summary>
public sealed partial class Wh40kBorgPdaSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private PdaSystem _pda = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kBorgPdaComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<Wh40kBorgPdaComponent, CartridgeLoaderNotificationSentEvent>(OnNotification);
        SubscribeLocalEvent<EntityRenamedEvent>(OnRenamed);
    }

    private void OnMapInit(Entity<Wh40kBorgPdaComponent> ent, ref MapInitEvent args)
    {
        SetOwner(ent, Name(ent));
    }

    private void OnRenamed(ref EntityRenamedEvent ev)
    {
        if (HasComp<Wh40kBorgPdaComponent>(ev.Uid))
            SetOwner(ev.Uid, ev.NewName);
    }

    private void SetOwner(EntityUid borg, string name)
    {
        if (TryComp<PdaComponent>(borg, out var pda))
            _pda.SetOwner(borg, pda, borg, name);
    }

    private void OnNotification(Entity<Wh40kBorgPdaComponent> ent, ref CartridgeLoaderNotificationSentEvent args)
    {
        if (!TryComp<ActorComponent>(ent, out var actor))
            return;

        var message = FormattedMessage.EscapeText(args.Message);
        var wrapped = Loc.GetString("pda-notification-message", ("header", args.Header), ("message", message));
        _chat.ChatMessageToOne(ChatChannel.Notifications, message, wrapped, EntityUid.Invalid, false, actor.PlayerSession.Channel);
    }
}
