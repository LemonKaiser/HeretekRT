namespace Content.Shared._WH40K.ShipPa;

/// <summary>
/// Smooths playback position against real time so server tick hiccups do not cause audible PA audio seeks.
/// </summary>
public struct Wh40kShipPaDrift
{
    public const float ResyncSeconds = 1f;
    public const float AgreeSeconds = 1f;
    public static readonly TimeSpan Disagreement = TimeSpan.FromSeconds(3);

    private float _position;
    private TimeSpan _realTime;
    private TimeSpan? _disagreeSince;
    private bool _anchored;

    public float Expected(float tickPosition, TimeSpan realTime, float length, bool loop)
    {
        if (!_anchored)
        {
            Anchor(tickPosition, realTime);
            return tickPosition;
        }

        var smooth = _position + (float) (realTime - _realTime).TotalSeconds;
        if (loop && length > 0f)
            smooth %= length;

        if (Distance(smooth, tickPosition, length, loop) <= AgreeSeconds)
        {
            _disagreeSince = null;
            return smooth;
        }

        _disagreeSince ??= realTime;
        if (realTime - _disagreeSince.Value < Disagreement)
            return smooth;

        Anchor(tickPosition, realTime);
        return tickPosition;
    }

    public static bool NeedsSeek(float actual, float expected, float length, bool loop)
    {
        return Distance(actual, expected, length, loop) > ResyncSeconds;
    }

    private static float Distance(float a, float b, float length, bool loop)
    {
        var error = Math.Abs(a - b);
        return loop && length > 0f ? Math.Min(error, Math.Abs(length - error)) : error;
    }

    private void Anchor(float position, TimeSpan realTime)
    {
        _position = position;
        _realTime = realTime;
        _disagreeSince = null;
        _anchored = true;
    }
}
