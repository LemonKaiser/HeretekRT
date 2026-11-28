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

    private void ApplyMarkingGlow(SpriteComponent sprite, string layerId)
    {
        if (!sprite.LayerMapTryGet(layerId, out var index) || !sprite.TryGetLayer(index, out var layer))
            return;

        var shader = layer.ShaderPrototype;
        if (shader == DisplacedGlow.Id)
            return;

        var glow = shader == Displaced.Id ? DisplacedGlow.Id : UnshadedShaderName;
        sprite.LayerSetShader(index, glow);
    }

    private void ApplyEyeGlow(HumanoidAppearanceComponent humanoid, SpriteComponent sprite)
    {
        var index = sprite.LayerMapReserveBlank(HumanoidVisualLayers.Eyes);
        if (MarkingGlowEnabled && humanoid.MarkingGlow.Contains(Wh40kMarkingGlowRules.Eyes))
        {
            sprite.LayerSetShader(index, UnshadedShaderName);
            return;
        }

        if (sprite.TryGetLayer(index, out var layer) && layer.ShaderPrototype == UnshadedShaderName)
            sprite.LayerSetShader(index, null, null);
    }
}
