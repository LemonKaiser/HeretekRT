using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

/// <summary>Small status beacon with a restrained outward ping.</summary>
public sealed class LobbySignalIndicator : Control
{
    private static readonly Color Gold = Color.FromHex("#E5C879");

    /// <summary>Normalized 0..1 ping phase.</summary>
    public float Pulse { get; set; }

    public LobbySignalIndicator()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize) => new(9f, 9f);

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var scale = UIScale;
        var centre = (Vector2) PixelSize * 0.5f;
        var phase = Math.Clamp(Pulse, 0f, 1f);
        var coreRadius = 3.25f * scale;

        handle.DrawCircle(centre, coreRadius + 1.5f * scale, Gold.WithAlpha(0.10f));
        handle.DrawCircle(centre, coreRadius, Gold);
        handle.DrawCircle(
            centre,
            coreRadius + (1.4f + phase * 5.2f) * scale,
            Gold.WithAlpha((1f - phase) * 0.30f),
            filled: false);
    }
}
