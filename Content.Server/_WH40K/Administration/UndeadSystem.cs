using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chat.Managers;
using Content.Shared._WH40K.Administration;
using Content.Shared.Chat;
using Content.Shared.Rejuvenate;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Administration;

public sealed partial class UndeadSystem : SharedUndeadSystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private RespiratorSystem _respirator = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<UndeadComponent, RejuvenateEvent>(OnRejuvenate);
    }

    private void OnRejuvenate(Entity<UndeadComponent> ent, ref RejuvenateEvent args)
    {
        if (TryComp<RespiratorComponent>(ent, out var respirator))
            _respirator.UpdateSaturation(ent, respirator.MaxSaturation - respirator.Saturation, respirator);
    }

    protected override void OnRecovered(Entity<UndeadComponent> ent)
    {
        if (!TryComp<ActorComponent>(ent, out var actor))
            return;

        var message = Loc.GetString("wh40k-undead-recovered",
            ("deaths", ent.Comp.Deaths), ("total", ent.Comp.TotalDamage));
        _chat.ChatMessageToOne(ChatChannel.Server, message, message, default, false,
            actor.PlayerSession.Channel, colorOverride: Color.Crimson);
    }
}
