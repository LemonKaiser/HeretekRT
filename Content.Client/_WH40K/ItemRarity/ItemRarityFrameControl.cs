using Content.Shared._WH40K.ItemRarity.Prototypes;
using Content.Shared._WH40K.ItemRarity.Systems;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.ItemRarity;

/// <summary>
/// Reusable rarity frame presenter for item slots and storage pieces.
/// It deliberately refreshes from the entity each frame so a replicated rarity
/// change is visible without rebuilding or reopening the surrounding UI.
/// </summary>
public sealed partial class ItemRarityFrameControl : Control
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;

    private EntityUid? _entity;
    private ProtoId<ItemRarityPrototype>? _cachedRarity;
    private ItemRarityPrototype? _rarity;

    public ItemRarityFrameControl()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Ignore;
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    public void SetEntity(EntityUid? entity)
    {
        if (_entity == entity)
            return;

        _entity = entity;
        _cachedRarity = null;
        _rarity = null;
    }

    /// <summary>
    /// Paints the frame into an arbitrary rectangle. Storage pieces use this
    /// overload to draw the frame below their icon and status overlays.
    /// </summary>
    public void DrawFrame(DrawingHandleScreen handle, UIBox2 area)
    {
        DrawFrame(handle, area, UIScale);
    }

    public void DrawFrame(DrawingHandleScreen handle, UIBox2 area, float uiScale)
    {
        UpdateFrame();

        if (_rarity == null || area.Width <= 0 || area.Height <= 0)
            return;

        ItemRarityPainter.DrawFrame(handle, area, uiScale, _rarity);
    }

    /// <summary>
    /// Draws one cell of a shape-aware storage outline. Only exposed edges are
    /// painted, so L-shaped and elongated items do not receive an empty
    /// rectangular frame around their bounding box.
    /// </summary>
    public void DrawStorageCellFrame(
        DrawingHandleScreen handle,
        UIBox2 area,
        bool top,
        bool bottom,
        bool left,
        bool right)
    {
        UpdateFrame();

        if (_rarity == null || area.Width <= 0 || area.Height <= 0)
            return;

        ItemRarityPainter.DrawStorageCellFrame(handle, area, UIScale, _rarity, top, bottom, left, right);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        DrawFrame(handle, PixelSizeBox);
    }

    private void UpdateFrame()
    {
        if (_entity is not { } entity ||
            !_entityManager.System<SharedItemRaritySystem>().TryGetRarity(entity, out var rarityId))
        {
            _cachedRarity = null;
            _rarity = null;
            return;
        }

        if (_cachedRarity == rarityId && _rarity != null)
            return;

        if (!_prototypeManager.TryIndex(rarityId, out ItemRarityPrototype? rarity))
        {
            _cachedRarity = null;
            _rarity = null;
            return;
        }

        _rarity = rarity;
        _cachedRarity = rarityId;
    }
}
