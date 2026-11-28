namespace Content.Shared._WH40K.FtlEffects;

/// <summary>Shapes a short build-up and departure flash on the authoritative jump clock.</summary>
public static class Wh40kFtlDepartureTiming
{
    public const float BuildDuration = 4f;
    public const float FadeDuration = 0.65f;
    public const float MotionDuration = 0.24f;
    public const float HoldDuration = 1f;

    public static bool Gone(TimeSpan now, TimeSpan departure, bool entered, bool arriving)
    {
        if (entered || arriving)
            return false;
        var seconds = (float) (now - departure).TotalSeconds;
        return seconds > 0f && seconds < HoldDuration;
    }

    public static float Motion(TimeSpan now, TimeSpan departure, bool entered, bool arriving)
    {
        var seconds = (float) (now - departure).TotalSeconds;
        if (arriving)
        {
            if (seconds < 0f || seconds >= MotionDuration)
                return 0f;
            var remaining = 1f - seconds / MotionDuration;
            return -remaining * remaining * remaining;
        }

        if (entered || seconds < -MotionDuration || seconds > 0f)
            return 0f;
        var progress = 1f + seconds / MotionDuration;
        return progress * progress * progress;
    }

    public static float Intensity(TimeSpan now, TimeSpan started, TimeSpan departure, bool entered)
    {
        if (entered)
        {
            var fade = Math.Clamp((float) (now - departure).TotalSeconds / FadeDuration, 0f, 1f);
            return 1f - fade * fade * (3f - 2f * fade);
        }

        var duration = Math.Clamp((float) (departure - started).TotalSeconds, 0.001f, BuildDuration);
        var progress = Math.Clamp(1f - (float) (departure - now).TotalSeconds / duration, 0f, 1f);
        return progress * progress * (3f - 2f * progress);
    }
}
