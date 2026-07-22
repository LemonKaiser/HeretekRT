using System;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

internal static class LobbyPanelOpacityHelper
{
    private static readonly Color FallbackColor = Color.FromHex("#25252ADD");

    public static StyleBox CreateOpacityStyle(StyleBox? source, float opacity, Color? fallbackColor = null)
    {
        opacity = Math.Clamp(opacity, 0f, 1f);

        if (TryCreateOpacityStyle(source, opacity, out var style))
            return style;

        var fallback = fallbackColor ?? FallbackColor;
        return new StyleBoxFlat
        {
            BackgroundColor = fallback.WithAlpha(fallback.A * opacity),
        };
    }

    private static bool TryCreateOpacityStyle(StyleBox? source, float opacity, out StyleBox style)
    {
        switch (source)
        {
            case StyleBoxFlat flat:
                style = new StyleBoxFlat(flat)
                {
                    // Keep the visual hierarchy encoded in the source style.
                    // Replacing alpha made deliberately subtle lobby layers into opaque cards.
                    BackgroundColor = flat.BackgroundColor.WithAlpha(flat.BackgroundColor.A * opacity),
                    BorderColor = flat.BorderColor.WithAlpha(flat.BorderColor.A * opacity),
                };
                return true;
            case StyleBoxTexture texture:
                style = new StyleBoxTexture(texture)
                {
                    Modulate = texture.Modulate.WithAlpha(texture.Modulate.A * opacity),
                };
                return true;
            case LobbyDrawerStyleBox drawer:
                style = new LobbyDrawerStyleBox(drawer)
                {
                    LeftColor = drawer.LeftColor.WithAlpha(drawer.LeftColor.A * opacity),
                    MiddleColor = drawer.MiddleColor.WithAlpha(drawer.MiddleColor.A * opacity),
                    RightColor = drawer.RightColor.WithAlpha(drawer.RightColor.A * opacity),
                    BorderColor = drawer.BorderColor.WithAlpha(drawer.BorderColor.A * opacity),
                };
                return true;
            default:
                style = default!;
                return false;
        }
    }
}
