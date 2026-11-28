using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Verbs;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Cyborgs;

/// <summary>
/// Allows a cyborg to buckle nearby patients onto its chassis strap for transport and evacuation.
/// </summary>
public sealed partial class Wh40kBorgRescueSystem : EntitySystem
{
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GetVerbsEvent<InteractionVerb>>(AddBuckleVerb);
        SubscribeLocalEvent<BuckleComponent, Wh40kBorgRescueDoAfterEvent>(OnBuckleDoAfter);
    }

    private void AddBuckleVerb(GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess ||
            !args.CanInteract ||
            args.User == args.Target ||
            !HasComp<BorgChassisComponent>(args.User) ||
            !TryComp<StrapComponent>(args.User, out var strap) ||
            !strap.Enabled ||
            !TryComp<BuckleComponent>(args.Target, out var buckle) ||
            buckle.BuckledTo != null ||
            !HasSpace(strap, buckle) ||
            !_interaction.InRangeUnobstructed(args.User, args.Target, buckle.Range))
            return;

        var target = args.Target;
        var user = args.User;
        args.Verbs.Add(new InteractionVerb
        {
            Act = () => StartBuckleDoAfter(target, user, strap.BuckleDoafterTime),
            Category = VerbCategory.Buckle,
            Text = Identity.Name(user, EntityManager),
        });
    }

    private void StartBuckleDoAfter(EntityUid target, EntityUid borg, float delay)
    {
        var doAfter = new DoAfterArgs(
            EntityManager,
            borg,
            delay,
            new Wh40kBorgRescueDoAfterEvent(),
            target,
            target,
            borg)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            AttemptFrequency = AttemptFrequency.EveryTick,
        };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnBuckleDoAfter(Entity<BuckleComponent> ent, ref Wh40kBorgRescueDoAfterEvent args)
    {
        if (args.Cancelled
            || args.Used is not { } borg
            || !HasComp<BorgChassisComponent>(borg)
            || !TryComp<StrapComponent>(borg, out var strap)
            || !strap.Enabled)
            return;

        _buckle.TryBuckle(ent.Owner, null, borg, ent.Comp, popup: false);
    }

    private bool HasSpace(StrapComponent strap, BuckleComponent buckle)
    {
        var available = strap.Size;
        foreach (var buckled in strap.BuckledEntities)
            available -= CompOrNull<BuckleComponent>(buckled)?.Size ?? 0;

        return available >= buckle.Size;
    }
}

[Serializable, NetSerializable]
public sealed partial class Wh40kBorgRescueDoAfterEvent : SimpleDoAfterEvent;
