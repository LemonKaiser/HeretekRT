using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;

namespace Content.Shared._WH40K.DeployableFieldBase;

public abstract partial class SharedDeployableFieldBaseSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<DeployableFieldBaseComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(Entity<DeployableFieldBaseComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.DeployTime,
            new DeployableFieldBaseDoAfterEvent(), ent.Owner, used: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            args.Handled = true;
    }
}
