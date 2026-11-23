using System.Numerics;
using Content.Client.Resources;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.UserInterface.WindowPopout;

/// <summary>
/// Title bar button that pops a window out into its own OS window, or docks it back.
/// </summary>
public sealed partial class Wh40kPopoutButton : TextureButton
{
    public const string StyleClassWindowPopoutButton = "Wh40kWindowPopoutButton";

    private const string PopOutTexture = "/Textures/_WH40K/Interface/popout.svg.192dpi.png";
    private const string DockTexture = "/Textures/_WH40K/Interface/dock.svg.192dpi.png";

    [Dependency] private IResourceCache _cache = default!;

    private bool _poppedOut;

    public Wh40kPopoutButton()
    {
        IoCManager.InjectDependencies(this);
        AddStyleClass(StyleClassWindowPopoutButton);
        Scale = new Vector2(0.375f);
        Margin = new Thickness(1);
        VerticalAlignment = VAlignment.Center;
        DefaultCursorShape = CursorShape.Hand;
        PoppedOut = false;
    }

    public bool PoppedOut
    {
        get => _poppedOut;
        set
        {
            _poppedOut = value;
            TextureNormal = _cache.GetTexture(value ? DockTexture : PopOutTexture);
            ToolTip = Loc.GetString(value ? "wh40k-window-popout-dock" : "wh40k-window-popout-pop-out");
        }
    }
}
