using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Content.Client._WH40K.UserInterface;

namespace Content.Client._WH40K.CharacterCreation;

internal static class Wh40kPersonalizationTheme
{
    // Compatibility helpers for character-specific code; all drawing and control styling lives in the shared theme.
    public static StyleBox Surface(string background = "#15120F", string border = "#56452D", int padding = 12, float radius = 5)
        => Wh40kInterfaceTheme.Surface(background, border, padding, padding, radius);

    public static StyleBox Button(string background = "#18140F", string border = "#79603D", int padding = 8)
        => Wh40kInterfaceTheme.Surface(background, border, padding, 3, 4);

    public static StyleBox Frame(string background = "#100D0AEB", string border = "#56452D", int padding = 6)
        => Wh40kInterfaceTheme.Surface(background, border, padding, padding, 3);

    public static void Apply(Control control) => Wh40kInterfaceTheme.ApplyControls(control);
    public static void UseSkinToneGradient(Slider slider) => Wh40kInterfaceTheme.UseSkinToneGradient(slider);

    public static void ToggleMenu(Popup popup, Control anchor, bool above = false)
    {
        if (popup.Visible)
        {
            popup.Close();
            return;
        }

        var root = anchor.Root;
        if (root == null)
            return;

        root.ModalRoot.AddChild(popup);
        var content = popup.GetChild(0);
        content.Measure(new Vector2(320, root.Size.Y));
        var size = content.DesiredSize;
        var position = anchor.GlobalPosition + new Vector2(0, above ? -size.Y - 4 : anchor.Size.Y + 4);
        position = Vector2.Clamp(position, Vector2.Zero, Vector2.Max(Vector2.Zero, root.Size - size));
        popup.Open(UIBox2.FromDimensions(position, size));
    }
}
