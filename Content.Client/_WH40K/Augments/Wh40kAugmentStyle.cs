using Robust.Client.Graphics;
using Content.Client._WH40K.CharacterCreation;

namespace Content.Client._WH40K.Augments;

public static class Wh40kAugmentStyle
{
    public static readonly Color Text = Color.FromHex("#E9E1D0");
    public static readonly Color SecondaryText = Color.FromHex("#ADA391");
    public static readonly Color SelectedText = Color.White;

    internal static StyleBox Panel(int padding) => Wh40kPersonalizationTheme.Surface("#15120F", "#56452D", padding);

    internal static StyleBox Card(int padding, bool selected = false)
        => selected
            ? Wh40kPersonalizationTheme.Surface("#392B1C", "#BE9E64", padding, 4)
            : Wh40kPersonalizationTheme.Surface("#1D1812", "#56452D", padding, 4);
}
