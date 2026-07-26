using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Draws the shared onboarding ornament mirrored without creating a duplicate asset.
/// </summary>
internal sealed class OnboardingMirroredTextureRect : TextureRect
{
    protected override void Draw(DrawingHandleScreen handle)
    {
        var previousTransform = handle.GetTransform();
        var mirror = Matrix3x2.CreateScale(-1f, 1f) * Matrix3x2.CreateTranslation(PixelSize.X, 0f);
        handle.SetTransform(mirror * previousTransform);

        try
        {
            base.Draw(handle);
        }
        finally
        {
            handle.SetTransform(previousTransform);
        }
    }
}
