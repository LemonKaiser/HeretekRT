using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Presentation-only overlays for the static lobby artwork.
/// The background texture itself is never translated, recoloured or replaced.
/// </summary>
public sealed class LobbyBackdropEffects : Control
{
    private static readonly ProtoId<ShaderPrototype> ShadeShaderId = "HeretekLobbyBackdropShade";

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private readonly ShaderInstance _shadeShader;

    public LobbyBackdropEffects()
    {
        IoCManager.InjectDependencies(this);
        _shadeShader = _prototypeManager.Index(ShadeShaderId).Instance();
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        if (size.X <= 1f || size.Y <= 1f)
            return;

        // One shader pass keeps the darkening continuous. Drawing it as a series
        // of translucent rectangles caused their seams to show on light artwork.
        handle.UseShader(_shadeShader);
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, size), Color.White);
        handle.UseShader(null);
    }
}
