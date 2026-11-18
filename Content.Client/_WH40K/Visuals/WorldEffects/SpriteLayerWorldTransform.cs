using System.Numerics;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Client.Utility;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._WH40K.Visuals.WorldEffects;

/// <summary>
/// Mirrors SpriteSystem.RenderSprite's layer transform so world effects follow the drawn pixels.
/// </summary>
internal static class SpriteLayerWorldTransform
{
    public static RsiDirection GetFrameDirection(SpriteComponent sprite, SpriteComponent.Layer layer,
        RsiDirectionType directions, RsiDirection matrixDirection)
    {
        var direction = sprite.EnableDirectionOverride ? sprite.DirectionOverride.Convert(directions) : matrixDirection;
        return direction.OffsetRsiDir(layer.DirOffset);
    }

    public static ContactShadowProjectedBounds GetFootBounds(SpriteSystem sprites, RsiVisualGeometryCache geometry,
        Entity<SpriteComponent> entity, Vector2 position, Angle rotation, Angle screenAngle, Angle eyeRotation,
        Vector2 screenRight, Vector2 screenUp)
    {
        var bounds = new ContactShadowProjectedBounds();
        if (sprites.TryGetLayer(entity.AsNullable(), "shoes", out var shoes, logMissing: false) &&
            IncludeOpaqueLayer(geometry, entity.Comp, shoes, position, rotation, screenAngle, eyeRotation,
                screenRight, screenUp, ref bounds))
            return bounds;

        if (sprites.TryGetLayer(entity.AsNullable(), HumanoidVisualLayers.LFoot, out var left, logMissing: false))
            IncludeOpaqueLayer(geometry, entity.Comp, left, position, rotation, screenAngle, eyeRotation,
                screenRight, screenUp, ref bounds, allowHidden: true);

        if (sprites.TryGetLayer(entity.AsNullable(), HumanoidVisualLayers.RFoot, out var right, logMissing: false))
            IncludeOpaqueLayer(geometry, entity.Comp, right, position, rotation, screenAngle, eyeRotation,
                screenRight, screenUp, ref bounds, allowHidden: true);

        return bounds;
    }

    public static bool IncludeOpaqueLayer(RsiVisualGeometryCache geometry, SpriteComponent sprite, SpriteComponent.Layer layer,
        Vector2 position, Angle rotation, Angle screenAngle, Angle eyeRotation, Vector2 axisX, Vector2 axisY,
        ref ContactShadowProjectedBounds bounds, bool allowHidden = false)
    {
        if ((!layer.Visible && !allowHidden) || layer.Blank || layer.Color.A <= 0.01f ||
            layer.CopyToShaderParameters != null || layer.ActualState is not { } state)
            return false;

        var matrixDirection = SpriteComponent.Layer.GetDirection(state.RsiDirections, screenAngle);
        var frameDirection = GetFrameDirection(sprite, layer, state.RsiDirections, matrixDirection);
        var opaque = geometry.GetOpaqueGeometry(state, frameDirection, layer.AnimationFrame);
        if (!opaque.Visible)
            return false;

        var transform = Get(sprite, layer, matrixDirection, position, rotation, screenAngle, eyeRotation);
        bounds.Include(opaque.Bounds, transform, axisX, axisY);
        return true;
    }

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
