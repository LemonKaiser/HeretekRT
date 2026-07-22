using Content.Shared._WH40K.ItemRarity.Prototypes;
using Content.Shared._WH40K.ItemRarity.Systems;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.ItemRarity;

/// <summary>
/// Examine panel that applies an item's rarity frame and creates its quality header.
/// </summary>
public sealed partial class ItemRarityExaminePanel : PanelContainer
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;

    private readonly ItemRarityFrameControl _rarityFrame = new();

    public ItemRarityExaminePanel()
    {
        IoCManager.InjectDependencies(this);
    }

    /// <summary>
    /// Applies the rarity frame and returns a header to insert into the panel content.
    /// Unknown targets deliberately keep the default panel and expose no rarity.
    /// </summary>
    public Control? ApplyRarity(EntityUid target, bool knowTarget)
    {
        _rarityFrame.SetEntity(null);
        PanelOverride = null;

        if (!knowTarget || !_entityManager.System<SharedItemRaritySystem>().TryGetRarity(target, out var rarityId))
            return null;

        var rarity = _prototypeManager.Index(rarityId);
        _rarityFrame.SetEntity(target);
        PanelOverride = ItemRarityVisuals.CreatePanelStyle(rarity);
        ModulateSelfOverride = Color.White.WithAlpha(0.98f);

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 7,
            HorizontalExpand = true,
            Margin = new Thickness(6, 0, 6, 2)
        };

        var badge = new ItemRarityBadgeControl();
        badge.SetRarity(rarity);
        header.AddChild(badge);

        var rarityName = Loc.GetString(rarity.Name);
        var qualityLabel = new Label
        {
            Text = rarityName,
            FontColorOverride = rarity.Color,
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
            ClipText = false
        };
        header.AddChild(qualityLabel);

        var headerPanel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(6, 2, 6, 3),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#070B10").WithAlpha(214),
                BorderColor = rarity.Color.WithAlpha(0.62f),
                BorderThickness = new Thickness(2, 0, 0, 0),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 2,
                ContentMarginBottomOverride = 2,
            }
        };
        headerPanel.AddChild(header);
        return headerPanel;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        _rarityFrame.DrawFrame(handle, PixelSizeBox, UIScale);
    }
}
