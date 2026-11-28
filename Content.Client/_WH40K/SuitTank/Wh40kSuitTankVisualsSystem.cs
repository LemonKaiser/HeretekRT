using Content.Shared._WH40K.SuitTank;
using Content.Shared.Clothing;
using Robust.Client.GameObjects;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.SuitTank;

/// <summary>
/// Renders the first gas tank stored inside a worn suit onto the wearer using its suit-storage sprite.
/// </summary>
public sealed partial class Wh40kSuitTankVisualsSystem : EntitySystem
{
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private Wh40kSuitTankStorageSystem _suitTank = default!;

    private const string TankSlot = "suitstorage";
    private const string LayerPrefix = "wh40k-suit-tank";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kSuitTankStorageComponent, EquipmentVisualsUpdatedEvent>(OnVisualsUpdated);
    }

    private void OnVisualsUpdated(EntityUid uid, Wh40kSuitTankStorageComponent comp, EquipmentVisualsUpdatedEvent args)
    {
        if (_suitTank.GetTanks(uid).FirstOrNull() is not { } tank
            || !TryComp<SpriteComponent>(args.Equipee, out var sprite))
        {
            return;
        }

        var ev = new GetEquipmentVisualsEvent(args.Equipee, TankSlot);
        RaiseLocalEvent(tank, ev);

        var tankRsi = CompOrNull<SpriteComponent>(tank)?.BaseRSI;
        var i = 0;
        foreach (var (_, data) in ev.Layers)
        {
            var key = $"{LayerPrefix}-{i++}";
            if (!args.RevealedLayers.Add(key))
                continue;

            var layer = _serialization.CreateCopy(data, notNullableOverride: true);
            layer.MapKeys = null;
            if (layer.RsiPath == null && layer.TexturePath == null && tankRsi != null)
                layer.RsiPath = tankRsi.Path.ToString();

            var index = _sprite.LayerMapReserve((args.Equipee, sprite), key);
            _sprite.LayerSetData((args.Equipee, sprite), index, layer);
        }
    }
}
