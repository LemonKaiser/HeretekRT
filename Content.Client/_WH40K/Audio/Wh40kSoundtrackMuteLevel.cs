namespace Content.Client._WH40K.Audio;

/// <summary>Music level from 1 (full) to 0 (silent), with a fade each way and a hold before it returns.</summary>
public sealed class Wh40kSoundtrackMuteLevel
{
    /// <summary>Seconds a full fade takes.</summary>
    public const float FadeSeconds = 1f;

    /// <summary>Seconds of quiet before the music returns.</summary>
    public const float HoldSeconds = 2f;

    /// <summary>Current level, 1 for full volume and 0 for silent.</summary>
    public float Level { get; private set; } = 1f;

    private float _quietFor;

    /// <summary>Advances the level by one frame, down while audible and up once the quiet has lasted the hold.</summary>
    public void Update(bool audible, float frameTime)
    {
        if (audible)
        {
            _quietFor = 0f;
            Level = MathF.Max(0f, Level - frameTime / FadeSeconds);
            return;
        }

        _quietFor += frameTime;
        if (_quietFor < HoldSeconds)
            return;

        Level = MathF.Min(1f, Level + frameTime / FadeSeconds);
    }

    /// <summary>Returns the level to full at once.</summary>
    public void Reset()
    {
        Level = 1f;
        _quietFor = 0f;
    }
}
