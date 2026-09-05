using Robust.Shared.Containers;

namespace Content.Shared._WH40K.Augments;

public abstract class SharedAugmentToolPanelSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<AugmentToolPanelActiveItemComponent, ContainerGettingRemovedAttemptEvent>(OnDropAttempt);
    }

    private void OnDropAttempt(Entity<AugmentToolPanelActiveItemComponent> ent, ref ContainerGettingRemovedAttemptEvent args)
    {
        args.Cancel();
    }
}
