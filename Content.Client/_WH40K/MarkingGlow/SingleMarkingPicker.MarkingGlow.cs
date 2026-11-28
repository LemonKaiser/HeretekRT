using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Humanoid;

public sealed partial class SingleMarkingPicker
{
    private CheckBox? _glowCheckBox;
    private bool _suppressGlowEvent;

    public event Action<bool>? OnGlowChanged;

    public void ConfigureGlow(string textLocKey, string tooltipLocKey)
    {
        if (_glowCheckBox != null)
            return;

        _glowCheckBox = new CheckBox
        {
            Text = Loc.GetString(textLocKey),
            ToolTip = Loc.GetString(tooltipLocKey),
            Visible = false,
            HorizontalAlignment = HAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        _glowCheckBox.OnToggled += args =>
        {
            if (!_suppressGlowEvent)
                OnGlowChanged?.Invoke(args.Pressed);
        };
        MarkingSelectorContainer.AddChild(_glowCheckBox);
    }

    public void SetGlowState(bool visible, bool pressed)
    {
        if (_glowCheckBox == null)
            return;

        _glowCheckBox.Visible = visible && Visible;
        _suppressGlowEvent = true;
        _glowCheckBox.Pressed = pressed;
        _suppressGlowEvent = false;
        _glowCheckBox.Disabled = EditingDisabled;
    }

    private void UpdateGlowDisabled()
    {
        if (_glowCheckBox != null)
            _glowCheckBox.Disabled = EditingDisabled;
    }
}
