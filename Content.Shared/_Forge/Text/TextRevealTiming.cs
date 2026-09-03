namespace Content.Shared._Forge.Text;

/// <summary>
///     Shared timing for text that is revealed progressively. Keeping this outside of Content.Client
///     lets speech audio follow the exact same rhythm as speech bubbles.
/// </summary>
public static class TextRevealTiming
{
    public const float ElementsPerSecond = 38f;
    /// <summary>
    ///     The maximum number of Unicode text elements shown by a world-space text presentation.
    ///     Longer text ends with an ellipsis instead of growing a speech bubble across the viewport.
    /// </summary>
    public const int MaxWorldTextElements = 256;

    public const float MinimumSpeedMultiplier = 0.25f;
    public const float MaximumSpeedMultiplier = 4f;

    public static float ClampSpeedMultiplier(float value)
        => float.IsFinite(value) ? Math.Clamp(value, MinimumSpeedMultiplier, MaximumSpeedMultiplier) : 1f;

    /// <summary>
    ///     Returns the time between this visible element and the next one at normal speed.
    /// </summary>
    public static float GetElementDelay(string element)
    {
        var delay = 1f / ElementsPerSecond;
        if (string.IsNullOrEmpty(element))
            return delay;

        return element switch
        {
            "!" or "?" or "¡" or "¿" => delay + 0.09f,
            "." or "…" or "。" => delay + 0.075f,
            "," or ";" or ":" or "、" => delay + 0.045f,
            "\n" or "\r" or "\r\n" => delay + 0.12f,
            _ when string.IsNullOrWhiteSpace(element) => delay * 0.55f,
            _ => delay,
        };
    }
}
