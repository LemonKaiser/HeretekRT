using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.UserInterface;

/// <summary>Native checkbox input behaviour with the approved switch indicator.</summary>
public sealed class Wh40kToggleSwitch : CheckBox
{
    public Wh40kToggleSwitch()
    {
        TextureRect.AddStyleClass("Wh40kSwitch");
    }
    protected override void DrawModeChanged() { base.DrawModeChanged(); Wh40kChoiceAppearance.Update(this); }
}

/// <summary>Native button groups provide the mutually exclusive radio behaviour.</summary>
public sealed class Wh40kRadioButton : CheckBox
{
    public Wh40kRadioButton()
    {
        TextureRect.AddStyleClass("Wh40kRadio");
    }
    protected override void DrawModeChanged() { base.DrawModeChanged(); Wh40kChoiceAppearance.Update(this); }
}

public sealed class Wh40kCheckBox : CheckBox
{
    protected override void DrawModeChanged() { base.DrawModeChanged(); Wh40kChoiceAppearance.Update(this); }
}

internal static class Wh40kChoiceAppearance
{
    internal static void Update(CheckBox control)
    {
        if (control.TextureRect == null || control.Label == null) return;
        control.TextureRect.ModulateSelfOverride = Color.White.WithAlpha(control.Disabled ? 0.38f : 1f);
        control.Label.FontColorOverride = Wh40kInterfaceTheme.Text.WithAlpha(control.Disabled ? 0.38f : 1f);
    }
}
