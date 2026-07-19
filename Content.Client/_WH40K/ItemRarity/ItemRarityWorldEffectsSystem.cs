using System.Numerics;
using Content.Shared._WH40K.ItemRarity.Systems;
using Content.Shared.Item;
using Content.Shared._WH40K.ItemRarity.Components;
using Content.Shared._WH40K.ItemRarity.Prototypes;
using Content.Client.Clickable;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.ItemRarity;

/// <summary>
/// Owns the client-only world overlay for rarity presentation.
/// </summary>
public sealed partial class ItemRarityWorldEffectsSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private SharedItemRaritySystem _itemRaritySystem = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private SpriteTreeSystem _spriteTree = default!;
    [Dependency] private ClickableSystem _clickableSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;

    private ItemRarityWorldEffectsRenderer _renderer = default!;
    private ItemRarityWorldEffectsOverlay _worldOverlay = default!;
    private ItemRarityWorldEffectsHoverOverlay _hoverOverlay = default!;
    private EntityUid? _hoveredEntity;
    private readonly List<HoveredEntity> _hoveredEntities = new();

    public override void Initialize()
    {
        base.Initialize();

        _renderer = new ItemRarityWorldEffectsRenderer(
            EntityManager,
            _prototypeManager,
            _configuration,
            _itemRaritySystem,
            () => _hoveredEntity);
        _worldOverlay = new ItemRarityWorldEffectsOverlay(_renderer);
        _hoverOverlay = new ItemRarityWorldEffectsHoverOverlay(_renderer);

        _overlayManager.AddOverlay(_worldOverlay);
        _overlayManager.AddOverlay(_hoverOverlay);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _renderer.BeginFrame();
        _hoveredEntity = FindHoveredItem();
    }

    public override void Shutdown()
    {
        _overlayManager.RemoveOverlay(_worldOverlay);
        _overlayManager.RemoveOverlay(_hoverOverlay);
        _renderer.Dispose();
        base.Shutdown();
    }

    private EntityUid? FindHoveredItem()
    {
        var mouseScreenPosition = _inputManager.MouseScreenPosition;
        if (!mouseScreenPosition.IsValid)
            return null;

        var eye = _eyeManager.CurrentEye;
        var mouseCoordinates = _eyeManager.PixelToMap(mouseScreenPosition);
        if (mouseCoordinates.MapId == MapId.Nullspace)
            return null;

        var itemQuery = GetEntityQuery<ItemComponent>();
        var rarityQuery = GetEntityQuery<ItemRarityComponent>();
        var clickableQuery = GetEntityQuery<ClickableComponent>();
        var transformQuery = GetEntityQuery<TransformComponent>();
        _hoveredEntities.Clear();

        foreach (var entity in _spriteTree.QueryAabb(
                     mouseCoordinates.MapId,
                     Box2.CenteredAround(mouseCoordinates.Position, new Vector2(1f, 1f))))
        {
            var uid = entity.Uid;
            if (!clickableQuery.TryGetComponent(uid, out var clickable))
                continue;

            if (!_clickableSystem.CheckClick(
                    (uid, clickable, entity.Component, entity.Transform),
                    mouseCoordinates.Position,
                    eye,
                    out var drawDepth,
                    out var renderOrder,
                    out var bottom))
            {
                continue;
            }

            _hoveredEntities.Add(new HoveredEntity(uid, drawDepth, renderOrder, bottom));
        }

        _hoveredEntities.Sort(static (left, right) =>
        {
            var result = right.DrawDepth.CompareTo(left.DrawDepth);
            if (result != 0)
                return result;

            result = right.RenderOrder.CompareTo(left.RenderOrder);
            if (result != 0)
                return result;

            result = -right.Bottom.CompareTo(left.Bottom);
            if (result != 0)
                return result;

            return right.Entity.CompareTo(left.Entity);
        });

        if (_hoveredEntities.Count == 0)
            return null;

        // Resolve the topmost clickable entity first. This prevents an item
        // behind a mob or another clickable sprite from being highlighted
        // through that sprite.
        var hovered = _hoveredEntities[0].Entity;
        if (!itemQuery.HasComponent(hovered) ||
            !transformQuery.TryGetComponent(hovered, out var hoveredTransform) ||
            !ItemRarityWorldEffectsRenderer.IsDirectlyInWorld(hoveredTransform) ||
            _containerSystem.IsEntityInContainer(hovered) ||
            !_itemRaritySystem.TryGetRarity(hovered, out var rarityId) ||
            !_prototypeManager.TryIndex(rarityId, out ItemRarityPrototype? rarity) ||
            (rarity.Tier == 1 && !rarityQuery.HasComponent(hovered)))
        {
            return null;
        }

        return hovered;
    }

    private readonly record struct HoveredEntity(
        EntityUid Entity,
        int DrawDepth,
        uint RenderOrder,
        float Bottom);
}
