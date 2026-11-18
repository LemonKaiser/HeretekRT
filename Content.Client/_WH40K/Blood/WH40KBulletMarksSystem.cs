using Content.Client.Clothing;
using Content.Shared._WH40K.Blood;
using Content.Shared.Clothing;
using Content.Shared.Humanoid;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Blood;

public sealed partial class WH40KBulletMarksSystem : EntitySystem
{
    private const string SkinLayer = "wh40k-bullet-marks-skin";
    private static readonly ResPath Art = new("_WH40K/Effects/bullet_marks.rsi");

    [Dependency] private SharedItemSystem _items = default!;
    [Dependency] private SpriteSystem _sprites = default!;

    private readonly HashSet<EntityUid> _changedEquipment = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KBulletMarksComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WH40KBulletMarksComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WH40KBulletMarksComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KBulletMarksComponent, GetEquipmentVisualsEvent>(OnGetVisuals,
            after: [typeof(ClientClothingSystem)]);
    }

    private void OnStartup(Entity<WH40KBulletMarksComponent> ent, ref ComponentStartup args)
        => UpdateVisuals(ent);

    private void OnState(Entity<WH40KBulletMarksComponent> ent, ref AfterAutoHandleStateEvent args)
        => UpdateVisuals(ent);

    private void OnShutdown(Entity<WH40KBulletMarksComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (ent.Comp.Material != WH40KBulletMarkMaterial.Skin)
            _changedEquipment.Add(ent);
        else if (TryComp<SpriteComponent>(ent, out var sprite))
            _sprites.RemoveLayer((ent.Owner, sprite), SkinLayer, logMissing: false);
    }

    public override void FrameUpdate(float frameTime)
    {
        // Inventory containers and item component states can arrive in either order in a game state.
        foreach (var item in _changedEquipment)
        {
            if (!TerminatingOrDeleted(item))
                _items.VisualsChanged(item);
        }

        _changedEquipment.Clear();
    }

    private void UpdateVisuals(Entity<WH40KBulletMarksComponent> ent)
    {
        if (ent.Comp.ActiveMarks == 0)
            return;

        if (ent.Comp.Material != WH40KBulletMarkMaterial.Skin)
        {
            _changedEquipment.Add(ent);
            return;
        }

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var target = (ent.Owner, sprite);
        var specifier = new SpriteSpecifier.Rsi(Art, GetState(ent.Comp));
        if (_sprites.LayerMapTryGet(target, SkinLayer, out var existing, logMissing: false))
            _sprites.LayerSetSprite(target, existing, specifier);
        else if (_sprites.LayerMapTryGet(target, HumanoidVisualLayers.Chest, out var chest, logMissing: false))
            _sprites.LayerMapSet(target, SkinLayer, _sprites.AddLayer(target, specifier, chest + 1));
    }

    private void OnGetVisuals(EntityUid uid, WH40KBulletMarksComponent marks, GetEquipmentVisualsEvent args)
    {
        if (marks.ActiveMarks == 0 || marks.Material == WH40KBulletMarkMaterial.Skin ||
            args.Layers.Count == 0 || args.Slot is not ("outerClothing" or "jumpsuit"))
            return;

        args.Layers.Add(($"wh40k-bullet-marks-{args.Slot}", new PrototypeLayerData
        {
            RsiPath = Art.ToString(),
            State = GetState(marks),
            Visible = true,
        }));
    }

    private static string GetState(WH40KBulletMarksComponent marks)
    {
        var material = marks.Material switch
        {
            WH40KBulletMarkMaterial.Cloth => "cloth",
            WH40KBulletMarkMaterial.Armor => "armor",
            _ => "skin",
        };
        return $"{material}{marks.ActiveMarks}";
    }
}
