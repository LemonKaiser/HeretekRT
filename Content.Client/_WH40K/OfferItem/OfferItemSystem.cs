using Content.Shared._WH40K.OfferItem;
using Content.Shared.Hands.Components;
using Content.Shared.Verbs;

namespace Content.Client._WH40K.OfferItem;

/// <summary>
/// Presents offer and accept verbs. The server owns every state transition and transfer.
/// </summary>
public sealed class OfferItemSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<OfferedItemComponent, GetVerbsEvent<InteractionVerb>>(OnReceiverVerbs);
        SubscribeLocalEvent<OfferingItemComponent, GetVerbsEvent<InteractionVerb>>(OnOffererVerbs);
    }

    private void OnReceiverVerbs(Entity<OfferedItemComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.User == ent.Owner ||
            args.Using is not { } item || !TryComp<HandsComponent>(args.User, out _))
        {
            return;
        }

        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("offer-item-verb-offer"),
            Act = () => RaiseNetworkEvent(new RequestOfferItemEvent(GetNetEntity(ent.Owner), GetNetEntity(item))),
            Priority = 1,
        });
    }

    private void OnOffererVerbs(Entity<OfferingItemComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.Target != args.User || ent.Comp.Item == null)
            return;

        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("offer-item-verb-accept"),
            Act = () => RaiseNetworkEvent(new AcceptOfferedItemEvent(GetNetEntity(ent.Owner))),
            Priority = 1,
        });
    }
}
