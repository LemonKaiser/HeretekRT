using Content.Shared._WH40K.Invisibility;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Invisibility;

/// <summary>
/// Applies an opacity shader only to a humanoid's body and markings, leaving equipped clothing visible.
/// </summary>
public sealed class WH40KPsykerInvisibilityVisualSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> ShaderPrototype = "WH40KPsykerInvisibility";

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly MarkingManager _marking = default!;
    private readonly Dictionary<EntityUid, ShaderInstance> _shaders = new();
    private readonly Dictionary<EntityUid, Dictionary<HumanoidVisualLayers, LayerShaderState>> _baseLayerShaders = new();
    private readonly Dictionary<EntityUid, Dictionary<string, LayerShaderState>> _markingLayerShaders = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerInvisibilityComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KPsykerInvisibilityComponent, AfterAutoHandleStateEvent>(OnState);
    }

    public override void Shutdown()
    {
        foreach (var uid in new List<EntityUid>(_shaders.Keys))
            RemoveShader(uid);

        _shaders.Clear();
        _baseLayerShaders.Clear();
        _markingLayerShaders.Clear();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WH40KPsykerInvisibilityComponent, HumanoidAppearanceComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var invisibility, out _, out _))
            ApplyShader(uid, invisibility);
    }

    private void OnState(Entity<WH40KPsykerInvisibilityComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        ApplyShader(entity.Owner, entity.Comp);
    }

    private void OnShutdown(Entity<WH40KPsykerInvisibilityComponent> entity, ref ComponentShutdown args)
    {
        RemoveShader(entity.Owner);
    }

    private void ApplyShader(EntityUid uid, WH40KPsykerInvisibilityComponent invisibility)
    {
        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid) ||
            !TryComp<SpriteComponent>(uid, out var sprite))
            return;

        var visibility = float.IsFinite(invisibility.ShaderVisibility)
            ? Math.Clamp(invisibility.ShaderVisibility, 0f, 1f)
            : 0f;
        var shader = GetShader(uid, visibility);
        var baseLayerShaders = GetBaseLayerShaders(uid);
        foreach (var layerKey in humanoid.BaseLayers.Keys)
        {
            if (sprite.LayerMapTryGet(layerKey, out var index))
                ApplyLayerShader(sprite, index, layerKey, shader, baseLayerShaders);
        }

        ApplyMarkingShader(sprite, humanoid, shader, GetMarkingLayerShaders(uid));
    }

    private void RemoveShader(EntityUid uid)
    {
        _baseLayerShaders.Remove(uid, out var baseLayerShaders);
        _markingLayerShaders.Remove(uid, out var markingLayerShaders);

        if (TryComp<SpriteComponent>(uid, out var sprite))
        {
            if (baseLayerShaders != null)
            {
                foreach (var (layerKey, previousShader) in baseLayerShaders)
                {
                    if (sprite.LayerMapTryGet(layerKey, out var index))
                        RestoreLayerShader(sprite, index, previousShader);
                }
            }

            if (markingLayerShaders != null)
            {
                foreach (var (layerKey, previousShader) in markingLayerShaders)
                {
                    if (sprite.LayerMapTryGet(layerKey, out var index))
                        RestoreLayerShader(sprite, index, previousShader);
                }
            }
        }

        if (_shaders.Remove(uid, out var shader))
            shader.Dispose();
    }

    private void ApplyMarkingShader(
        SpriteComponent sprite,
        HumanoidAppearanceComponent humanoid,
        ShaderInstance shader,
        Dictionary<string, LayerShaderState> previousShaders)
    {
        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                if (!_marking.TryGetMarking(marking, out var prototype))
                    continue;

                foreach (var spriteSpecifier in prototype.Sprites)
                {
                    if (spriteSpecifier is not SpriteSpecifier.Rsi rsi)
                        continue;

                    var layerKey = $"{marking.MarkingId}-{rsi.RsiState}";
                    if (sprite.LayerMapTryGet(layerKey, out var index))
                        ApplyLayerShader(sprite, index, layerKey, shader, previousShaders);
                }
            }
        }
    }

    private Dictionary<HumanoidVisualLayers, LayerShaderState> GetBaseLayerShaders(EntityUid uid)
    {
        if (!_baseLayerShaders.TryGetValue(uid, out var shaders))
            _baseLayerShaders[uid] = shaders = new();

        return shaders;
    }

    private Dictionary<string, LayerShaderState> GetMarkingLayerShaders(EntityUid uid)
    {
        if (!_markingLayerShaders.TryGetValue(uid, out var shaders))
            _markingLayerShaders[uid] = shaders = new();

        return shaders;
    }

    private static void ApplyLayerShader<TKey>(
        SpriteComponent sprite,
        int index,
        TKey layerKey,
        ShaderInstance shader,
        Dictionary<TKey, LayerShaderState> previousShaders) where TKey : notnull
    {
        if (!sprite.TryGetLayer(index, out var layer))
            return;

        if (!previousShaders.TryGetValue(layerKey, out _) || layer.Shader != shader)
        {
            previousShaders[layerKey] = new LayerShaderState(layer.Shader, layer.ShaderPrototype);
            sprite.LayerSetShader(index, shader, ShaderPrototype.Id);
        }
    }

    private static void RestoreLayerShader(SpriteComponent sprite, int index, LayerShaderState previousShader)
    {
        if (previousShader.Instance != null)
            sprite.LayerSetShader(index, previousShader.Instance, previousShader.Prototype?.Id);
        else if (previousShader.Prototype != null)
            sprite.LayerSetShader(index, previousShader.Prototype.Value.Id);
        else
            sprite.LayerSetShader(index, (ShaderInstance?) null);
    }

    private ShaderInstance GetShader(EntityUid uid, float visibility)
    {
        if (_shaders.TryGetValue(uid, out var existing))
        {
            existing.SetParameter("visibility", visibility);
            return existing;
        }

        var shader = _prototypeManager.Index(ShaderPrototype).InstanceUnique();
        shader.SetParameter("visibility", visibility);
        _shaders[uid] = shader;
        return shader;
    }

    private readonly record struct LayerShaderState(
        ShaderInstance? Instance,
        ProtoId<ShaderPrototype>? Prototype);
}
