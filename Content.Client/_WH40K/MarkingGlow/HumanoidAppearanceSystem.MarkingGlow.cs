using Content.Shared._WH40K.MarkingGlow;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.Humanoid;

public sealed partial class HumanoidAppearanceSystem
{
    private static readonly ProtoId<ShaderPrototype> Displaced = "DisplacedDraw";
    private static readonly ProtoId<ShaderPrototype> DisplacedGlow = "Wh40kMarkingGlowDisplaced";
    private const string UnshadedShaderName = "unshaded";

    private bool MarkingGlows(HumanoidAppearanceComponent humanoid, MarkingPrototype marking)
    {
        return MarkingGlowEnabled && Wh40kMarkingGlowRules.Glows(humanoid.MarkingGlow, marking);
    }

    private void ApplyMarkingGlow(Entity<SpriteComponent> sprite, string layerId)
    {
        if (!_sprite.LayerMapTryGet((sprite.Owner, sprite.Comp), layerId, out var index, false)
            || !_sprite.TryGetLayer((sprite.Owner, sprite.Comp), index, out var layer, false))
            return;

        var shader = layer.ShaderPrototype;
        if (shader == DisplacedGlow.Id)
            return;

        var glow = shader == Displaced.Id ? DisplacedGlow.Id : UnshadedShaderName;
        sprite.Comp.LayerSetShader(index, glow);
    }

    private void ApplyEyeGlow(Entity<HumanoidAppearanceComponent, SpriteComponent> entity)
    {
        var (uid, humanoid, sprite) = entity;
        var index = _sprite.LayerMapReserve((uid, sprite), HumanoidVisualLayers.Eyes);
        if (MarkingGlowEnabled && humanoid.MarkingGlow.Contains(Wh40kMarkingGlowRules.Eyes))
        {
            sprite.LayerSetShader(index, UnshadedShaderName);
            return;
        }

        if (_sprite.TryGetLayer((uid, sprite), index, out var layer, false)
            && layer.ShaderPrototype == UnshadedShaderName)
            sprite.LayerSetShader(index, null, null);
    }
}
