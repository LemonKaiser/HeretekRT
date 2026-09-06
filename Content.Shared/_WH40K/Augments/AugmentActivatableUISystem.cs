namespace Content.Shared._WH40K.Augments;

public sealed partial class AugmentActivatableUISystem : EntitySystem
{
    [Dependency] private AugmentSystem _augment = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AugmentActivatableUIComponent, AugmentActionEvent>(OnAugmentAction);
    }

    private void OnAugmentAction(Entity<AugmentActivatableUIComponent> augment, ref AugmentActionEvent args)
    {
        if (_augment.GetBody(augment) is not { } body || augment.Comp.Key is not { } key ||
            !_ui.HasUi(augment, key))
        {
            return;
        }

        _ui.OpenUi(augment.Owner, key, body);
        args.Handled = true;
    }
}
