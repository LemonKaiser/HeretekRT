using System.Numerics;
using Content.Client._WH40K.SectorMap.UI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Draws the single command-deck spine behind the lobby controls.  Keeping it as a
/// decoration lets the line continue through the music attribution without turning
/// individual panels into hard cards.
/// </summary>
public sealed class LobbyDeckDecoration : Control
{
    private static readonly Color Line = Color.FromHex("#B69754");
    private static readonly Color BrightGold = Color.FromHex("#E5C879");
    private const float LobbyCompassScale = 0.99f;
    private readonly KoronusCurrentSystemBeacon _currentSystemBeacon = new();

    /// <summary>Общее время лобби для астролябии текущей системы.</summary>
    public float AnimationTime { get; set; }

    public LobbyDeckDecoration()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        if (size.X <= 1f || size.Y <= 1f)
            return;

        var scale = UIScale;
        var lineWidth = MathF.Max(1f, scale);
        handle.DrawRect(
            UIBox2.FromDimensions(Vector2.Zero, new Vector2(lineWidth, size.Y)),
            Line.WithAlpha(0.43f));

        var centre = new Vector2(lineWidth * 0.5f, MathF.Min(size.Y - 8f * scale, 95f * scale));
        _currentSystemBeacon.Draw(
            handle,
            centre,
            4.05f * scale,
            BrightGold,
            AnimationTime,
            AnimationTime,
            scale,
            scaleFactor: LobbyCompassScale);
    }
}
