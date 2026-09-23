using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._WH40K.Visuals.WorldEffects;

/// <summary>
/// Mirrors SpriteSystem.RenderSprite's layer transform so world effects follow the drawn pixels.
/// </summary>
internal static class SpriteLayerWorldTransform
{
    public static Matrix3x2 Get(
        SpriteComponent sprite,
        SpriteComponent.Layer layer,
        RsiDirection direction,
        Vector2 worldPosition,
        Angle worldRotation,
        Angle screenAngle,
        Angle eyeRotation)
    {
        var cardinal = Angle.Zero;
        if (!sprite.NoRotation && sprite.SnapCardinals)
            cardinal = screenAngle.RoundToCardinalAngle();

        var entityMatrix = Matrix3Helpers.CreateTransform(
            worldPosition,
            sprite.NoRotation ? -eyeRotation : worldRotation - cardinal);
        var spriteMatrix = Matrix3x2.Multiply(sprite.LocalMatrix, entityMatrix);

        if (sprite.GranularLayersRendering)
        {
            entityMatrix = layer.RenderingStrategy switch
            {
                LayerRenderingStrategy.Default =>
                    Matrix3Helpers.CreateTransform(worldPosition, worldRotation),
                LayerRenderingStrategy.NoRotation =>
                    Matrix3Helpers.CreateTransform(worldPosition, -eyeRotation),
                LayerRenderingStrategy.SnapToCardinals =>
                    Matrix3Helpers.CreateTransform(worldPosition, worldRotation - screenAngle.RoundToCardinalAngle()),
                _ => entityMatrix,
            };
            spriteMatrix = Matrix3x2.Multiply(sprite.LocalMatrix, entityMatrix);
        }

        layer.GetLayerDrawMatrix(direction, out var layerMatrix);
        return Matrix3x2.Multiply(layerMatrix, spriteMatrix);
    }
}
