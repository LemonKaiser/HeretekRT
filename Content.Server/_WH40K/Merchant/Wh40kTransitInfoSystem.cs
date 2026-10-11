using System.Linq;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Shared._WH40K.Merchant;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Merchant;

/// <summary>Read-only view of the existing authoritative sector graph. No jump/payment/booking handlers.</summary>
public sealed class Wh40kTransitInfoSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kTransitInfoComponent, BoundUIOpenedEvent>(Opened);
        SubscribeLocalEvent<Wh40kTransitInfoComponent, Wh40kTransitRefresh>(Refresh);
    }
    public bool Open(EntityUid body, EntityUid npc) => HasComp<ActorComponent>(body) && HasComp<Wh40kTransitInfoComponent>(npc) &&
        _interaction.InRangeUnobstructed(body, npc, 3) && _ui.TryOpenUi(npc, Wh40kTransitUiKey.Key, body);
    private void Opened(Entity<Wh40kTransitInfoComponent> ent, ref BoundUIOpenedEvent ev)
    { if (ev.UiKey.Equals(Wh40kTransitUiKey.Key)) Update(ent, ev.Actor); }
    private void Refresh(EntityUid uid, Wh40kTransitInfoComponent comp, Wh40kTransitRefresh ev) => Update(uid, ev.Actor);
    private void Update(EntityUid uid, EntityUid body)
    {
        if (!TryComp(body, out ActorComponent? actor) || actor.PlayerSession.AttachedEntity != body ||
            !_ui.GetActors(uid, Wh40kTransitUiKey.Key).Contains(body) || !_interaction.InRangeUnobstructed(body, uid, 3)) return;
        _ui.SetUiState(uid, Wh40kTransitUiKey.Key, new Wh40kTransitState(_sector.GetInterfaceState(Transform(body).MapID, false)));
    }
}
