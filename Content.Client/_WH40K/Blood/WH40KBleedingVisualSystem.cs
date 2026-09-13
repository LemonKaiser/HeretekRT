using Content.Shared._WH40K.Blood;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Blood;

/// <summary>
///     Adds Arcane's animated blood-drip layer to bleeding humanoids.
/// </summary>
public sealed class WH40KBleedingVisualSystem : EntitySystem
{
    private static readonly ResPath BleedingRsi = new("/Textures/_WH40K/Effects/bleeding_damage.rsi");

    private static readonly (HumanoidVisualLayers Layer, string StatePrefix)[] BodyLayers =
    [
        (HumanoidVisualLayers.Chest, "Chest"),
        (HumanoidVisualLayers.Head, "Head"),
        (HumanoidVisualLayers.LArm, "LArm"),
        (HumanoidVisualLayers.RArm, "RArm"),
        (HumanoidVisualLayers.LLeg, "LLeg"),
        (HumanoidVisualLayers.RLeg, "RLeg"),
        (HumanoidVisualLayers.LHand, "LHand"),
        (HumanoidVisualLayers.RHand, "RHand"),
        (HumanoidVisualLayers.LFoot, "LFoot"),
        (HumanoidVisualLayers.RFoot, "RFoot"),
    ];

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KBleedingVisualComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WH40KBleedingVisualComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WH40KBleedingVisualComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<WH40KBleedingVisualComponent> entity, ref ComponentStartup args)
    {
        Apply(entity);
    }

    private void OnState(Entity<WH40KBleedingVisualComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        Apply(entity);
    }

    private void OnShutdown(Entity<WH40KBleedingVisualComponent> entity, ref ComponentShutdown args)
    {
        if (!TryComp(entity, out SpriteComponent? sprite))
            return;

        foreach (var (bodyLayer, _) in BodyLayers)
        {
            var layerKey = GetLayerKey(bodyLayer);
            if (!sprite.LayerMapTryGet(layerKey, out var layer))
                continue;

            sprite.LayerMapRemove(layerKey);
            sprite.RemoveLayer(layer);
        }
    }

    private void Apply(Entity<WH40KBleedingVisualComponent> entity)
    {
        if (!HasComp<HumanoidAppearanceComponent>(entity) || !TryComp(entity, out SpriteComponent? sprite))
            return;

        var severity = entity.Comp.Severity == WH40KBleedingVisualSeverity.Severe ? "Severe" : "Minor";
        foreach (var (bodyLayer, statePrefix) in BodyLayers)
        {
            var layerKey = GetLayerKey(bodyLayer);
            if (!sprite.LayerMapTryGet(layerKey, out var layer))
                layer = sprite.LayerMapReserveBlank(layerKey);

            sprite.LayerSetRSI(layer, BleedingRsi);
            sprite.LayerSetState(layer, $"{statePrefix}_{severity}");
            sprite.LayerSetVisible(layer, true);
        }
    }

    private static string GetLayerKey(HumanoidVisualLayers layer) => $"WH40KBleeding{layer}";
}
