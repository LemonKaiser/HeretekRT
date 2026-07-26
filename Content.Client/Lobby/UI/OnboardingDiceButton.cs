using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Draws the compact d6 sprite without its large transparent tile padding.
/// </summary>
public sealed class OnboardingDiceButton : TextureButton
{
    // The d6 sprite occupies 13x14 pixels in its 32x32 RSI tile. A one-pixel
    // margin keeps its outline intact while allowing the icon to fill the button.
    private static readonly UIBox2 D6SpriteRegion = new(8f, 9f, 23f, 25f);

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();

        ModulateSelfOverride = DrawMode switch
        {
            DrawModeEnum.Hover => Color.FromHex("#FFF0B5"),
            DrawModeEnum.Pressed => Color.FromHex("#C8A855"),
            DrawModeEnum.Disabled => Color.FromHex("#6B6250"),
            _ => Color.FromHex("#B69754"),
        };
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var texture = TextureNormal;
        if (texture is null)
        {
            base.Draw(handle);
            return;
        }

        var sourceSize = D6SpriteRegion.Size;
        var scale = MathF.Min(PixelSizeBox.Width / sourceSize.X, PixelSizeBox.Height / sourceSize.Y);
        var targetSize = sourceSize * scale;
        var targetPosition = PixelSizeBox.TopLeft + (PixelSizeBox.Size - targetSize) / 2f;
        // TextureButton itself does not apply Control.Modulate while drawing. This custom crop used to
        // therefore ignore every hover, pressed and disabled colour selected in DrawModeChanged().
        handle.DrawTextureRectRegion(
            texture,
            UIBox2.FromDimensions(targetPosition, targetSize),
            D6SpriteRegion,
            ModulateSelfOverride ?? Color.White);
    }
}
