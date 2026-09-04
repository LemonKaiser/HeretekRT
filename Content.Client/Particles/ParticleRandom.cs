namespace Content.Client.Particles;

/// <summary>
/// Small deterministic PRNG kept per emitter so replicated one-shot effects do not depend on unrelated client RNG use.
/// </summary>
internal sealed class ParticleRandom
{
    private uint _state;

    public ParticleRandom(int seed)
    {
        _state = (uint) seed;
        if (_state == 0)
            _state = 0xA341316Cu;
    }

    public float NextFloat(float minimum, float maximum)
    {
        if (minimum == maximum)
            return minimum;

        // Keep the generated fraction in [0, 1) even after conversion to float.
        var fraction = (NextUInt() >> 8) * (1f / (1u << 24));
        return minimum + (maximum - minimum) * fraction;
    }

    private uint NextUInt()
    {
        var value = _state;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        _state = value;
        return value;
    }
}
