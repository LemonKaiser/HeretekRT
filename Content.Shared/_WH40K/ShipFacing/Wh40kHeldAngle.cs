namespace Content.Shared._WH40K.ShipFacing;

/// <summary>
/// A view rotation that holds still while the cursor-facing key is down so a still cursor marks
/// a fixed world point, and eases back to the live rotation once released.
/// </summary>
public sealed class Wh40kHeldAngle
{
    public static readonly TimeSpan ReturnTime = TimeSpan.FromSeconds(0.35);

    private Angle? _held;
    private Angle _from;
    private TimeSpan _releasedAt;
    private bool _returning;

    public Angle Apply(Angle live, bool hold, TimeSpan now)
    {
        if (hold)
        {
            _held ??= Returning(live, now);
            _returning = false;
            return _held.Value;
        }

        if (_held is { } held)
        {
            _from = held;
            _releasedAt = now;
            _returning = true;
            _held = null;
        }

        return Returning(live, now);
    }

    private Angle Returning(Angle live, TimeSpan now)
    {
        if (!_returning)
            return live;

        var t = (float) ((now - _releasedAt) / ReturnTime);
        if (t >= 1f)
        {
            _returning = false;
            return live;
        }

        t = Math.Clamp(t, 0f, 1f);
        var eased = t * t * (3f - 2f * t);
        return _from + Angle.ShortestDistance(_from, live) * eased;
    }
}
