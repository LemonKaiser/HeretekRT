using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.UserInterface;

internal sealed class Wh40kCollapseButton : TextureButton
{
    internal Action? ToggleAction;
    internal bool IsCollapsed;
    public Wh40kCollapseButton() { OnPressed += _ => ToggleWindow(); }
    internal void ToggleWindow() => ToggleAction?.Invoke();
}
