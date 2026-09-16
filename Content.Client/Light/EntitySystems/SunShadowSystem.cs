using System.Diagnostics.Contracts;
using System.Numerics;
using Content.Client.GameTicking.Managers;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Client.Light.EntitySystems;

public sealed partial class SunShadowSystem : SharedSunShadowSystem
{
    [Dependency] private ClientGameTicker _ticker = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MetaDataSystem _metadata = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var mapQuery = AllEntityQuery<SunShadowCycleComponent, SunShadowComponent>();
        while (mapQuery.MoveNext(out var uid,  out var cycle, out var shadow))
        {
            if (!cycle.Running || cycle.Directions.Count == 0 || cycle.Duration <= TimeSpan.Zero)
                continue;

            var pausedTime = _metadata.GetPauseTime(uid);

            var elapsed = (float) (_timing.CurTime
                .Add(cycle.Offset)
                .Subtract(_ticker.RoundStartTimeSpan)
                .Subtract(pausedTime)
                .TotalSeconds);
            var duration = (float) cycle.Duration.TotalSeconds;
            var time = elapsed % duration;
            if (time < 0f)
                time += duration;

            var (direction, alpha) = GetShadow((uid, cycle), time);
            shadow.Direction = direction;
            shadow.Alpha = alpha;
        }
    }

    [Pure]
    public (Vector2 Direction, float Alpha) GetShadow(Entity<SunShadowCycleComponent> entity, float time)
    {
        var directions = entity.Comp.Directions;
        if (directions.Count == 0 || entity.Comp.Duration <= TimeSpan.Zero)
            return (Vector2.Zero, 0f);

        // The values are stored as percentages of the total duration so that changing the duration
        // changes the cycle speed without changing each transition. Normalize here too because this
        // method is public and may receive an unnormalized time during a map transition.
        var ratio = time / (float) entity.Comp.Duration.TotalSeconds;
        if (float.IsNaN(ratio) || float.IsInfinity(ratio))
            return (directions[0].Direction, directions[0].Alpha);

        ratio %= 1f;
        if (ratio < 0f)
            ratio += 1f;

        var currentIndex = directions.Count - 1;
        var wrapped = true;
        for (var i = directions.Count - 1; i >= 0; i--)
        {
            if (ratio < directions[i].Ratio)
                continue;

            currentIndex = i;
            wrapped = false;
            break;
        }

        var current = directions[currentIndex];
        var next = directions[(currentIndex + 1) % directions.Count];
        var nextRatio = currentIndex == directions.Count - 1 ? next.Ratio + 1f : next.Ratio;
        var interpolationRatio = wrapped ? ratio + 1f : ratio;
        var range = nextRatio - current.Ratio;
        if (range <= 0f)
            return (current.Direction, current.Alpha);

        var diff = Math.Clamp((interpolationRatio - current.Ratio) / range, 0f, 1f);

        // We lerp angle + length separately as we don't want a straight-line lerp and want the rotation to be consistent.
        var currentAngle = current.Direction.ToAngle();
        var nextAngle = next.Direction.ToAngle();

        var angle = Angle.Lerp(currentAngle, nextAngle, diff);
        // This is to avoid getting weird issues where the angle gets pretty close but length still noticeably catches up.
        var lengthDiff = MathF.Pow(diff, 1f / 2f);
        var length = float.Lerp(current.Direction.Length(), next.Direction.Length(), lengthDiff);

        var vector = angle.ToVec() * length;
        var alpha = float.Lerp(current.Alpha, next.Alpha, diff);
        return (vector, alpha);
    }
}
