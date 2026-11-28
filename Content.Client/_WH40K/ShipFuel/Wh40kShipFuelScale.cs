using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.ShipFuel;

public sealed class Wh40kShipFuelScale : Control
{
    private static readonly Color TrackColor = Color.FromHex("#3A3124");
    private static readonly Color TickColor = Color.FromHex("#978368");

    public float Fraction { get; set; }
    public Color FillColor { get; set; }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var width = MathF.Max(1f, PixelWidth - 1f);
        var trackHeight = PixelHeight * 0.45f;
        handle.DrawRect(new UIBox2(0, 0, width, trackHeight), TrackColor);
        if (Fraction > 0f)
            handle.DrawRect(new UIBox2(0, 0, width * Fraction, trackHeight), FillColor);

        for (var i = 0; i <= 20; i++)
        {
            var x = MathF.Round(width * i / 20f);
            var height = i % 5 == 0 ? PixelHeight : PixelHeight * 0.75f;
            handle.DrawLine(new Vector2(x, trackHeight), new Vector2(x, height), TickColor);
        }
    }
}
