using Content.Shared._WH40K.ItemRarity.Prototypes;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.ItemRarity;

/// <summary>
/// Maps rarity prototypes to client-only presentation styles.
/// The frame and badge are painted in code; gameplay and shared prototype data
/// do not depend on texture paths.
/// </summary>
internal static class ItemRarityVisuals
{
    public static readonly Thickness PanelContentMargin = new(10, 9, 10, 9);

    public static StyleBoxFlat CreatePanelStyle(ItemRarityPrototype rarity)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#080C12").WithAlpha(238),
            BorderColor = rarity.Color.WithAlpha(0.42f),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = PanelContentMargin.Left,
            ContentMarginTopOverride = PanelContentMargin.Top,
            ContentMarginRightOverride = PanelContentMargin.Right,
            ContentMarginBottomOverride = PanelContentMargin.Bottom,
        };
    }
}
