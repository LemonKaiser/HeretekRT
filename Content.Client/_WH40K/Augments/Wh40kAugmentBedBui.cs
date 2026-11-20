using Content.Shared._WH40K.Augments;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Augments;

public sealed class Wh40kAugmentBedBui(EntityUid owner, Enum key) : BoundUserInterface(owner, key)
{
    private Wh40kAugmentBedWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<Wh40kAugmentBedWindow>();
        _window.Install += option => SendMessage(new Wh40kAugmentBedInstall(option.Item, option.Zone));
        if (State is Wh40kAugmentBedState current)
            _window.Apply(current);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is Wh40kAugmentBedState current)
            _window?.Apply(current);
    }
}
