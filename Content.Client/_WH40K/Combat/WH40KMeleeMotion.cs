namespace Content.Client._WH40K.Combat;

/// <summary>Client-only poses. Reach is a fraction of the visual swing radius, and time of the animation length.</summary>
internal sealed record WH40KMeleeMotion(float Duration, float Radius, WH40KMeleePose[] Poses, float FadeStart = 0.52f)
{
    public static readonly WH40KMeleeMotion Thrust = new(0.19f, 0.90f,
    [
        new(0f, 12f, 0.38f, 8f),
        new(0.12f, 9f, 0.31f, 5f),
        new(0.32f, 0f, 1f, 0f),
        new(0.53f, 0f, 0.93f, 0f),
        new(1f, -7f, 0.43f, -4f),
    ]);

    public static readonly WH40KMeleeMotion Blade = new(0.24f, 0.98f,
    [
        new(0f, 47f, 0.51f, 17f),
        new(0.12f, 64f, 0.46f, 18f),
        new(0.32f, 0f, 1f, 8f),
        new(0.55f, -62f, 0.93f, 0f),
        new(1f, -77f, 0.60f, -12f),
    ]);

    public static readonly WH40KMeleeMotion Heavy = new(0.34f, 0.92f,
    [
        new(0f, 46f, 0.44f, -10f),
        new(0.18f, 65f, 0.45f, -23f),
        new(0.36f, 13f, 1f, -55f),
        new(0.53f, -19f, 0.96f, -40f),
        new(1f, -44f, 0.57f, -25f),
    ]);

    public static readonly WH40KMeleeMotion Chain = new(0.22f, 0.90f,
    [
        new(0f, 0f, 0.44f, -5f),
        new(0.20f, 9f, 0.77f, 7f),
        new(0.42f, 16f, 1f, 12f),
        new(0.57f, -1f, 1.04f, -3f),
        new(0.76f, -29f, 0.99f, -20f),
        new(0.86f, -34f, 0.87f, -17f),
        new(1f, -28f, 0.51f, -11f),
    ], FadeStart: 0.82f);
}

internal readonly record struct WH40KMeleePose(float Time, float Orbit, float Reach, float Tilt);
