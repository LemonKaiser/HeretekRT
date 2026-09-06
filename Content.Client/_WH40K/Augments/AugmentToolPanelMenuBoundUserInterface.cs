using Content.Shared._WH40K.Augments;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Augments;

public sealed partial class AugmentToolPanelMenuBoundUserInterface : BoundUserInterface
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IInputManager _input = default!;

    private AugmentToolPanelMenu? _menu;

    public AugmentToolPanelMenuBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<AugmentToolPanelMenu>();
        _menu.SetEntity(Owner);
        _menu.SendSwitchMessage += SendSwitchMessage;
        _menu.OpenCenteredAt(_input.MouseScreenPosition.Position / _clyde.ScreenSize);
    }

    private void SendSwitchMessage(EntityUid? tool)
    {
        SendMessage(new AugmentToolPanelSwitchMessage(_entities.GetNetEntity(tool)));
    }
}
