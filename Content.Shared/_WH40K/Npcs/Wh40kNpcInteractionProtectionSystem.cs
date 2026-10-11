using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Pulling.Events;
using Content.Shared.Verbs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Hands.Components;

namespace Content.Shared._WH40K.Npcs;

/// <summary>Block pulling and player seat removal on both authoritative and predicted NPC interactions.</summary>
public sealed class Wh40kNpcInteractionProtectionSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<Wh40kNpcProtectionComponent, BeingPulledAttemptEvent>(OnPull);
        SubscribeLocalEvent<Wh40kNpcProtectionComponent, UnbuckleAttemptEvent>(OnUnbuckle);
        SubscribeLocalEvent<Wh40kNpcProtectionComponent, GetVerbsEvent<InteractionVerb>>(OnNpcVerbs,
            after: [typeof(SharedBuckleSystem)]);
        // Native uncuff attempts are directed at the user, not broadcast or raised on the target.
        // Starting the native uncuff do-after requires hands, including non-human users.
        SubscribeLocalEvent<HandsComponent, UncuffAttemptEvent>(OnUncuff);
    }

    private void OnPull(EntityUid uid, Wh40kNpcProtectionComponent comp, BeingPulledAttemptEvent args)
        // Native CanPull also gates the pull verb, before creating predicted joints, hand items or HUD alerts.
        => args.Cancel();

    private void OnUnbuckle(EntityUid uid, Wh40kNpcProtectionComponent comp, ref UnbuckleAttemptEvent args)
    {
        // Furniture deletion and protected home recovery use the native no-user cleanup path.
        if (args.User != null) args.Cancelled = true;
    }

    private void OnNpcVerbs(EntityUid uid, Wh40kNpcProtectionComponent comp, GetVerbsEvent<InteractionVerb> args)
        => args.Verbs.RemoveWhere(verb => verb.Text == Loc.GetString("verb-categories-unbuckle"));

    private void OnUncuff(EntityUid uid, HandsComponent comp, ref UncuffAttemptEvent args)
    {
        if (HasComp<Wh40kNpcProtectionComponent>(args.Target))
            args.Cancelled = true;
    }

}
