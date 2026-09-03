using Content.Shared._Forge.Text;

namespace Content.Shared._Forge.Barks;

/// <summary>
///     Builds a compact, deterministic sequence of bark sounds from spoken text.
///     The sequence is generated on the server so clients do not need the spoken text itself.
/// </summary>
public static class ExpressiveBarkPlanner
{
    /// <summary>
    ///     Hard cap for one spoken message. Longer messages are sampled by time buckets,
    ///     while punctuation is preferred inside each bucket.
    /// </summary>
    public const int MaxBarksPerMessage = 96;

    private const float MinimumPitch = 0.25f;
    private const float MaximumPitch = 4f;

    /// <summary>
    ///     Creates audible beats at word starts and punctuation. This preserves expressive
    ///     reactions to punctuation without producing a delayed sound for every letter.
    ///     Unicode text elements are used rather than UTF-16 characters, so combining
    ///     characters and surrogate pairs stay intact.
    /// </summary>
    public static BarkSoundEventData[] CreatePlan(
        string message,
        float playbackSpeed,
        float basePitch,
        float expression,
        int soundVariantCount,
        int seed)
    {
        if (string.IsNullOrWhiteSpace(message) || soundVariantCount <= 0)
            return [];

        var speed = TextRevealTiming.ClampSpeedMultiplier(playbackSpeed);
        var pitch = ClampFinite(basePitch, MinimumPitch, MaximumPitch, 1f);
        var expressionAmount = ClampFinite(expression, 0f, 2f, 1f);
        var symbols = CollectSymbols(message, speed);
        if (symbols.Count == 0)
            return [];

        var selectedSymbols = SelectSymbols(symbols);
        var state = unchecked((uint) seed);
        var lastSoundIndex = -1;
        var result = new BarkSoundEventData[selectedSymbols.Count];

        for (var i = 0; i < selectedSymbols.Count; i++)
        {
            var symbol = selectedSymbols[i];
            var (pitchScale, volumeOffset) = GetTone(symbol.Kind, pitch, expressionAmount, ref state);
            var soundIndex = PickSoundIndex(soundVariantCount, lastSoundIndex, ref state);
            lastSoundIndex = soundIndex;

            result[i] = new BarkSoundEventData(
                symbol.Offset,
                soundIndex,
                pitchScale,
                volumeOffset);
        }

        return result;
    }

    private static List<BarkSymbol> CollectSymbols(string message, float speed)
    {
        var result = new List<BarkSymbol>();
        var offset = 0f;
        var isInsideWord = false;
        var elements = new List<string>(TextRevealTiming.MaxWorldTextElements);

        using var enumerator = UnicodeTextElements.Enumerate(message).GetEnumerator();
        while (elements.Count < TextRevealTiming.MaxWorldTextElements && enumerator.MoveNext())
        {
            elements.Add(enumerator.Current);
        }

        // TypewriterText reserves the final visible element for an ellipsis. Do the same for
        // barks, then add one terminal beat at exactly the ellipsis' reveal time.
        var isTruncated = enumerator.MoveNext();
        var sourceElementCount = isTruncated ? elements.Count - 1 : elements.Count;

        for (var index = 0; index < sourceElementCount; index++)
        {
            var element = elements[index];

            if (string.IsNullOrWhiteSpace(element))
            {
                isInsideWord = false;
                offset += TextRevealTiming.GetElementDelay(element) / speed;
                continue;
            }

            var kind = Classify(element);
            if (!isInsideWord || IsExpressiveSymbol(kind))
                result.Add(new BarkSymbol(offset, kind));

            isInsideWord = true;
            offset += TextRevealTiming.GetElementDelay(element) / speed;
        }

        if (isTruncated)
            result.Add(new BarkSymbol(offset, BarkSymbolKind.Terminal));

        return result;
    }

    private static bool IsExpressiveSymbol(BarkSymbolKind kind)
        => kind is BarkSymbolKind.Symbol or BarkSymbolKind.Pause or BarkSymbolKind.Terminal or
            BarkSymbolKind.Question or BarkSymbolKind.Exclamation;

    private static List<BarkSymbol> SelectSymbols(List<BarkSymbol> symbols)
    {
        if (symbols.Count <= MaxBarksPerMessage)
            return symbols;

        var result = new List<BarkSymbol>(MaxBarksPerMessage);
        for (var bucket = 0; bucket < MaxBarksPerMessage; bucket++)
        {
            var start = bucket * symbols.Count / MaxBarksPerMessage;
            var end = Math.Max(start + 1, (bucket + 1) * symbols.Count / MaxBarksPerMessage);
            var selected = symbols[start];

            for (var i = start + 1; i < end; i++)
            {
                if (GetPriority(symbols[i].Kind) > GetPriority(selected.Kind))
                    selected = symbols[i];
            }

            result.Add(selected);
        }

        return result;
    }

    private static BarkSymbolKind Classify(string element)
    {
        return element switch
        {
            "!" or "¡" => BarkSymbolKind.Exclamation,
            "?" or "¿" => BarkSymbolKind.Question,
            "." or "…" or "。" => BarkSymbolKind.Terminal,
            "," or ";" or ":" or "、" => BarkSymbolKind.Pause,
            _ when char.IsUpper(element, 0) => BarkSymbolKind.Uppercase,
            _ when char.IsDigit(element, 0) => BarkSymbolKind.Digit,
            _ when char.IsPunctuation(element, 0) || char.IsSymbol(element, 0) => BarkSymbolKind.Symbol,
            _ => BarkSymbolKind.Normal,
        };
    }

    private static int GetPriority(BarkSymbolKind kind)
    {
        return kind switch
        {
            BarkSymbolKind.Exclamation => 6,
            BarkSymbolKind.Question => 5,
            BarkSymbolKind.Terminal => 4,
            BarkSymbolKind.Pause => 3,
            BarkSymbolKind.Symbol => 2,
            BarkSymbolKind.Uppercase => 1,
            _ => 0,
        };
    }

    private static (float PitchScale, float VolumeOffset) GetTone(
        BarkSymbolKind kind,
        float basePitch,
        float expression,
        ref uint state)
    {
        var pitchVariation = NextSigned(ref state) * (0.03f + expression * 0.04f);
        var volumeOffset = NextSigned(ref state) * (0.25f + expression * 0.40f);
        var pitchScale = basePitch * (1f + pitchVariation);

        switch (kind)
        {
            case BarkSymbolKind.Uppercase:
                pitchScale *= 1.05f + expression * 0.03f;
                volumeOffset += 0.35f + expression * 0.20f;
                break;

            case BarkSymbolKind.Digit:
                pitchScale *= 0.96f;
                volumeOffset -= 0.15f;
                break;

            case BarkSymbolKind.Symbol:
                pitchScale *= 1.08f;
                volumeOffset += 0.20f;
                break;

            case BarkSymbolKind.Pause:
                pitchScale *= 0.94f;
                volumeOffset -= 0.55f;
                break;

            case BarkSymbolKind.Terminal:
                pitchScale *= 0.88f;
                volumeOffset -= 0.35f;
                break;

            case BarkSymbolKind.Question:
                pitchScale *= 1.14f + expression * 0.06f;
                volumeOffset += 0.70f + expression * 0.35f;
                break;

            case BarkSymbolKind.Exclamation:
                pitchScale *= 1.22f + expression * 0.08f;
                volumeOffset += 1.55f + expression * 0.60f;
                break;
        }

        return (Math.Clamp(pitchScale, MinimumPitch, MaximumPitch), Math.Clamp(volumeOffset, -3f, 3f));
    }

    private static int PickSoundIndex(int count, int previous, ref uint state)
    {
        if (count == 1)
            return 0;

        var selected = (int) (NextUInt(ref state) % (uint) count);
        if (selected == previous)
            selected = (selected + 1 + (int) (NextUInt(ref state) % (uint) (count - 1))) % count;

        return selected;
    }

    private static float NextSigned(ref uint state)
        => NextUInt(ref state) / (float) uint.MaxValue * 2f - 1f;

    private static uint NextUInt(ref uint state)
    {
        state = state * 1664525u + 1013904223u;
        return state;
    }

    private static float ClampFinite(float value, float minimum, float maximum, float fallback)
        => float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private readonly record struct BarkSymbol(float Offset, BarkSymbolKind Kind);

    private enum BarkSymbolKind : byte
    {
        Normal,
        Uppercase,
        Digit,
        Symbol,
        Pause,
        Terminal,
        Question,
        Exclamation,
    }
}
