using System.Numerics;
using Content.Shared._WH40K.ItemRarity.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.ItemRarity;

/// <summary>
/// Compact code-drawn rarity emblem used by the examine header.
/// </summary>
public sealed class ItemRarityBadgeControl : Control
{
    private ItemRarityPrototype? _rarity;

    public ItemRarityBadgeControl()
    {
        MinSize = new Vector2(24, 20);
        MaxSize = new Vector2(24, 20);
        MouseFilter = MouseFilterMode.Ignore;
    }

    public void SetRarity(ItemRarityPrototype rarity)
    {
        _rarity = rarity;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_rarity != null)
            ItemRarityPainter.DrawBadge(handle, PixelSizeBox, UIScale, _rarity);
    }
}
