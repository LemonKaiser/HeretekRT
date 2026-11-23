using Content.Client.Stylesheets;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Controls;

public sealed class MonotoneButton : Button
{
    /// <summary>
    /// Specifies the color of the button's background element
    /// </summary>
    private Color _backgroundColor = new(0.2f, 0.2f, 0.2f);
    public Color BackgroundColor
    {
        get => _backgroundColor;
        set { _backgroundColor = value; UpdateAppearance(); }
    }

    /// <summary>
    /// Describes the general shape of the button (i.e., open vs closed).
    /// </summary>
    public MonotoneButtonShape Shape
    {
        get { return _shape; }
        set { _shape = value; UpdateAppearance(); }
    }

    private MonotoneButtonShape _shape = MonotoneButtonShape.Closed;

    public MonotoneButton()
    {
        UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        StyleBoxOverride = null;
        foreach (var shape in new[] { StyleBase.ButtonOpenLeft, StyleBase.ButtonOpenRight, StyleBase.ButtonOpenBoth })
            RemoveStyleClass(shape);
        if (Shape != MonotoneButtonShape.Closed)
            AddStyleClass(Shape switch
            {
                MonotoneButtonShape.OpenLeft => StyleBase.ButtonOpenLeft,
                MonotoneButtonShape.OpenRight => StyleBase.ButtonOpenRight,
                _ => StyleBase.ButtonOpenBoth,
            });
        if (Label != null)
        {
            Label.ModulateSelfOverride = null;
            Label.FontColorOverride = Pressed && !Disabled && BackgroundColor != new Color(0.2f, 0.2f, 0.2f)
                ? BackgroundColor : null;
        }
        Modulate = Color.White;
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        UpdateAppearance();
    }
}

public enum MonotoneButtonShape : byte
{
    Closed = 0,
    OpenLeft = 1,
    OpenRight = 2,
    OpenBoth = 3
}
