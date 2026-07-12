using System.Linq;
using System.Numerics;
using Content.Client.Clothing;
using Content.Client.Humanoid;
using Content.Client.Inventory;
using Content.Client.Items.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Dialogue;

/// <summary>
/// Creates isolated local entities for dialogue portraits so scene-mode previews do not depend on live PVS, lighting or
/// mutable UI state.
/// </summary>
public sealed class DialoguePreviewSystem : EntitySystem
{
    private const SlotFlags PreviewVisualSlotFlags =
        SlotFlags.HEAD
        | SlotFlags.EYES
        | SlotFlags.EARS
        | SlotFlags.MASK
        | SlotFlags.OUTERCLOTHING
        | SlotFlags.INNERCLOTHING
        | SlotFlags.NECK
        | SlotFlags.BACK
        | SlotFlags.BELT
        | SlotFlags.GLOVES
        | SlotFlags.LEGS
        | SlotFlags.FEET
        | SlotFlags.SUITSTORAGE
        | SlotFlags.BALACLAVA
        | SlotFlags.ARMBANDLEFT
        | SlotFlags.ARMBANDRIGHT
        | SlotFlags.HELMETCOVER
        | SlotFlags.HELMETATTACHMENT;

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ClientClothingSystem _clothing = default!;
    [Dependency] private readonly ClientInventorySystem _inventory = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly ItemSystem _item = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public EntityUid? TryCreatePreview(EntityUid source)
    {
        if (TryComp(source, out HumanoidAppearanceComponent? humanoid)
            && _prototypeManager.TryIndex<SpeciesPrototype>(humanoid.Species, out var species))
        {
            var dummy = Spawn(species.DollPrototype, MapCoordinates.Nullspace);
            EnsureComp<DialoguePreviewMarkerComponent>(dummy);

            CopyHumanoidAppearance(source, dummy, humanoid);
            CopyEquipment(source, dummy);
            SyncHumanoidLayerState(source, dummy);
            StripPreviewDisplacementLayers(dummy);
            SetUnshaded(dummy);

            return dummy;
        }

        return TryCreateSpritePreview(source);
    }

    /// <summary>
    /// Creates a presentation-only portrait from an entity prototype. This supports named cast members who
    /// are not physically present in the current scene, without ever spawning them on the server or map.
    /// </summary>
    public EntityUid? TryCreatePrototypePreview(EntProtoId prototype)
    {
        if (!_prototypeManager.HasIndex<EntityPrototype>(prototype))
            return null;

        var preview = Spawn(prototype, MapCoordinates.Nullspace);
        EnsureComp<DialoguePreviewMarkerComponent>(preview);
        StripPreviewDisplacementLayers(preview);
        SetUnshaded(preview);
        return preview;
    }

    public void DeletePreview(ref EntityUid? previewEntity)
    {
        if (previewEntity is { } entity && Exists(entity))
            Del(entity);

        previewEntity = null;
    }

    private void CopyHumanoidAppearance(EntityUid source, EntityUid dummy, HumanoidAppearanceComponent sourceHumanoid)
    {
        if (!TryComp(dummy, out HumanoidAppearanceComponent? dummyHumanoid))
            return;

        _humanoid.CloneAppearance(source, dummy, sourceHumanoid, dummyHumanoid);
        dummyHumanoid.ClientOldMarkings = new MarkingSet();
        dummyHumanoid.HideLayersOnEquip = new HashSet<HumanoidVisualLayers>(sourceHumanoid.HideLayersOnEquip);
        dummyHumanoid.PermanentlyHidden = new HashSet<HumanoidVisualLayers>(sourceHumanoid.PermanentlyHidden);
        dummyHumanoid.HiddenLayers = new Dictionary<HumanoidVisualLayers, SlotFlags>(sourceHumanoid.HiddenLayers);

        RefreshHumanoidSprite(dummy, dummyHumanoid);
    }

    private void SyncHumanoidLayerState(EntityUid source, EntityUid dummy)
    {
        if (!TryComp(source, out HumanoidAppearanceComponent? sourceHumanoid)
            || !TryComp(dummy, out HumanoidAppearanceComponent? dummyHumanoid))
        {
            return;
        }

        dummyHumanoid.PermanentlyHidden = new HashSet<HumanoidVisualLayers>(sourceHumanoid.PermanentlyHidden);
        dummyHumanoid.HiddenLayers = new Dictionary<HumanoidVisualLayers, SlotFlags>(sourceHumanoid.HiddenLayers);

        RefreshHumanoidSprite(dummy, dummyHumanoid);
    }

    private void CopyEquipment(EntityUid source, EntityUid dummy)
    {
        if (!_inventory.TryGetSlots(source, out var sourceSlots)
            || !_inventory.TryGetSlots(dummy, out var dummySlots))
        {
            return;
        }

        var dummySlotNames = dummySlots.Select(slot => slot.Name).ToHashSet();

        foreach (var slot in sourceSlots)
        {
            if ((slot.SlotFlags & PreviewVisualSlotFlags) == SlotFlags.NONE
                || !dummySlotNames.Contains(slot.Name)
                || !_inventory.TryGetSlotEntity(source, slot.Name, out var equipped)
                || equipped == null
                || !TryComp(equipped.Value, out MetaDataComponent? metadata)
                || metadata.EntityPrototype == null)
            {
                continue;
            }

            var previewItem = Spawn(metadata.EntityPrototype.ID, MapCoordinates.Nullspace);
            CopyItemVisualState(equipped.Value, previewItem);
            SetUnshaded(previewItem);

            if (!_inventory.TryEquip(dummy, previewItem, slot.Name, true, true))
                Del(previewItem);
        }
    }

    private void CopyItemVisualState(EntityUid sourceItem, EntityUid previewItem)
    {
        if (TryComp(sourceItem, out ItemComponent? sourceItemComponent)
            && TryComp(previewItem, out ItemComponent? previewItemComponent))
        {
            _item.CopyVisuals(previewItem, sourceItemComponent, previewItemComponent);
        }

        if (TryComp(sourceItem, out ClothingComponent? sourceClothing)
            && TryComp(previewItem, out ClothingComponent? previewClothing))
        {
            _clothing.CopyVisuals(previewItem, sourceClothing, previewClothing);
        }

        if (!TryComp(sourceItem, out AppearanceComponent? sourceAppearance))
            return;

        TryComp(previewItem, out AppearanceComponent? previewAppearance);
        _appearance.CopyData((sourceItem, sourceAppearance), (previewItem, previewAppearance));
    }

    private EntityUid? TryCreateSpritePreview(EntityUid source)
    {
        if (!TryComp(source, out MetaDataComponent? metadata)
            || metadata.EntityPrototype == null
            || !TryComp(source, out SpriteComponent? sourceSprite))
        {
            return null;
        }

        var dummy = Spawn(metadata.EntityPrototype.ID, MapCoordinates.Nullspace);
        EnsureComp<DialoguePreviewMarkerComponent>(dummy);

        if (TryComp(source, out AppearanceComponent? sourceAppearance))
        {
            TryComp(dummy, out AppearanceComponent? dummyAppearance);
            _appearance.CopyData((source, sourceAppearance), (dummy, dummyAppearance));
        }

        TryComp(dummy, out SpriteComponent? dummySprite);
        _sprite.CopySprite((source, sourceSprite), (dummy, dummySprite));
        StripPreviewDisplacementLayers(dummy);
        SetUnshaded(dummy);
        return dummy;
    }

    private void RefreshHumanoidSprite(EntityUid uid, HumanoidAppearanceComponent humanoid)
    {
        if (TryComp(uid, out SpriteComponent? sprite))
        {
            // Dialogue previews are local snapshots; apply profile scale directly instead of changing common humanoid code.
            var width = humanoid.Width <= 0.005f ? 1.0f : humanoid.Width;
            var height = humanoid.Height <= 0.005f ? 1.0f : humanoid.Height;
            sprite.Scale = new Vector2(width, height);
        }

        var ev = new AfterAutoHandleStateEvent(default!);
        RaiseComponentEvent(uid, humanoid, ref ev);
    }

    private void SetUnshaded(EntityUid uid)
    {
        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        // Portraits must ignore map lighting; otherwise equipment layers disappear in dark/FOV-heavy scenes.
        for (var i = 0; i < sprite.AllLayers.Count(); i++)
        {
            if (!sprite.TryGetLayer(i, out var layer)
                || layer.CopyToShaderParameters != null
                || layer.ShaderPrototype == "DisplacedStencilDraw")
            {
                continue;
            }

            sprite.LayerSetShader(i, "unshaded");
        }
    }

    private void StripPreviewDisplacementLayers(EntityUid uid)
    {
        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        var copyLayerIndices = new List<int>();

        for (var i = 0; i < sprite.AllLayers.Count(); i++)
        {
            if (!sprite.TryGetLayer(i, out var layer))
                continue;

            if (layer.CopyToShaderParameters != null)
            {
                copyLayerIndices.Add(i);
                continue;
            }

            if (layer.ShaderPrototype == "DisplacedStencilDraw")
                sprite.LayerSetShader(i, "unshaded");
        }

        foreach (var index in copyLayerIndices.OrderDescending())
        {
            sprite.RemoveLayer(index);
        }

        if (!TryComp(uid, out InventorySlotsComponent? inventorySlots))
            return;

        foreach (var keySet in inventorySlots.VisualLayerKeys.Values)
        {
            keySet.RemoveWhere(static key => key.EndsWith("-displacement"));
        }
    }
}
