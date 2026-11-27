using Content.Shared._WH40K.Synthetics;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Synthetics;

[UsedImplicitly]
public sealed class Wh40kChassisPanelBoundUserInterface : BoundUserInterface
{
    private Wh40kChassisPanelWindow? _window;

    public Wh40kChassisPanelBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<Wh40kChassisPanelWindow>();
        _window.OnIsolate += (part, isolate) => SendMessage(new Wh40kChassisIsolatePartMessage(part, isolate));
        _window.OnDetach += part => SendMessage(new Wh40kChassisDetachPartMessage(part));
        _window.OnDisconnect += organ => SendMessage(new Wh40kChassisDisconnectOrganMessage(organ));
        _window.OnToggleBay += () => SendMessage(new Wh40kChassisToggleBayMessage());
        _window.OnReboot += () => SendMessage(new Wh40kChassisRebootMessage());
        _window.OnOverclock += on => SendMessage(new Wh40kChassisOverclockMessage(on));
        _window.OnInstallAugment += (item, zone) => SendMessage(new Wh40kChassisInstallAugmentMessage(item, zone));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is Wh40kChassisPanelState panelState)
            _window?.Update(panelState);
    }
}
