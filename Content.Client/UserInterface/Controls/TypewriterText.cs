using System.Text;
using Content.Shared._Forge.Text;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Controls;

/// <summary>
///     Timing for progressively revealing visible text elements. A text element is a Unicode grapheme,
///     rather than a UTF-16 character, so composed letters and emoji are never split while revealing.
/// </summary>
public sealed class TypewriterRevealPlan
{
    public const float CharactersPerSecond = TextRevealTiming.ElementsPerSecond;

    private readonly float[] _revealTimes;

    public int ElementCount => _revealTimes.Length;
    public TimeSpan Duration { get; }

    private TypewriterRevealPlan(float[] revealTimes, TimeSpan duration)
    {
        _revealTimes = revealTimes;
        Duration = duration;
    }

    public static TypewriterRevealPlan Create(
        IReadOnlyList<string> elements,
        TimeSpan maximumDuration,
        float speedMultiplier = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDuration, TimeSpan.Zero);
        speedMultiplier = TextRevealTiming.ClampSpeedMultiplier(speedMultiplier);

        if (elements.Count == 0)
            return new TypewriterRevealPlan([], TimeSpan.Zero);

        var revealTimes = new float[elements.Count];
        var revealTime = 0f;
        for (var i = 0; i < elements.Count; i++)
        {
            revealTimes[i] = revealTime;
            revealTime += TextRevealTiming.GetElementDelay(elements[i]) / speedMultiplier;
        }

        // TypewriterText bounds the visible prefix before it reaches this point. Rescaling this
        // plan would make longer messages reveal faster, defeating the accessibility speed setting.
        return new TypewriterRevealPlan(revealTimes, TimeSpan.FromSeconds(revealTimes[^1]));
    }

    public int GetVisibleElementCount(TimeSpan elapsed)
    {
        var elapsedSeconds = Math.Max(0f, (float) elapsed.TotalSeconds);
        var count = 0;
        while (count < _revealTimes.Length && _revealTimes[count] <= elapsedSeconds)
        {
            count++;
        }

        return count;
    }
}

/// <summary>
///     A bounded plain-text typewriter. It keeps only the visible part of an oversized text,
///     while retaining an ellipsis as its final reveal element.
/// </summary>
public sealed class TypewriterText
{
    private readonly List<string> _textElements;

    public TypewriterRevealPlan RevealPlan { get; }
    public int SourceElementCount => _textElements.Count;
    public bool IsTruncated { get; }

    private TypewriterText(
        List<string> textElements,
        bool isTruncated,
        TimeSpan maximumDuration,
        float speedMultiplier)
    {
        _textElements = textElements;
        IsTruncated = isTruncated;

        var presentationElements = new List<string>(textElements);
        if (isTruncated)
            presentationElements.Add("…");

        RevealPlan = TypewriterRevealPlan.Create(presentationElements, maximumDuration, speedMultiplier);
    }

    public static TypewriterText Create(
        string text,
        int maximumElements,
        TimeSpan maximumDuration,
        float speedMultiplier = 1f)
        => Create(EnumerateTextElements(text), maximumElements, maximumDuration, speedMultiplier);

    public static TypewriterText Create(
        IEnumerable<string> elements,
        int maximumElements,
        TimeSpan maximumDuration,
        float speedMultiplier = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumElements, 1);

        var speed = TextRevealTiming.ClampSpeedMultiplier(speedMultiplier);
        // Callers that intentionally reveal the complete server-limited message use int.MaxValue
        // as the logical limit. Do not turn that into an equally large up-front allocation.
        var candidates = new List<string>(Math.Min(maximumElements, 256));
        var hasMoreElements = false;
        foreach (var element in elements)
        {
            if (candidates.Count >= maximumElements)
            {
                hasMoreElements = true;
                break;
            }

            candidates.Add(element);
        }

        var selected = new List<string>(candidates.Count);
        var truncated = false;
        var revealTime = 0f;
        var maximumSeconds = (float) maximumDuration.TotalSeconds;
        for (var i = 0; i < candidates.Count; i++)
        {
            var element = candidates[i];
            var hasMoreAfterElement = i + 1 < candidates.Count || hasMoreElements;
            var nextRevealTime = revealTime + TextRevealTiming.GetElementDelay(element) / speed;

            // Reserve room for the trailing ellipsis before accepting another element. This keeps
            // a world-space message bounded without accelerating its per-character cadence.
            if (maximumSeconds > 0f &&
                hasMoreAfterElement &&
                selected.Count > 0 &&
                nextRevealTime > maximumSeconds)
            {
                truncated = true;
                break;
            }

            selected.Add(element);
            revealTime = nextRevealTime;
        }

        truncated |= hasMoreElements;

        // The visual budget includes the ellipsis. Reserving its slot makes truncation obvious
        // immediately instead of requiring a 257th reveal step after a 256-element message.
        if (truncated && selected.Count == maximumElements)
            selected.RemoveAt(selected.Count - 1);

        return new TypewriterText(selected, truncated, maximumDuration, speedMultiplier);
    }

    public string GetVisibleText(int visibleElementCount)
    {
        var visible = Math.Clamp(visibleElementCount, 0, RevealPlan.ElementCount);
        var sourceCount = Math.Min(visible, SourceElementCount);
        var builder = new StringBuilder();

        for (var i = 0; i < sourceCount; i++)
        {
            builder.Append(_textElements[i]);
        }

        if (IsTruncated && visible > SourceElementCount)
            builder.Append('…');

        return builder.ToString();
    }

    public static IEnumerable<string> EnumerateTextElements(string text)
        => UnicodeTextElements.Enumerate(text);
}

/// <summary>
///     Typewriter wrapper for rich text. Formatting tags are preserved, and inline chat emoji are revealed
///     as one visual element instead of exposing a partial markup tag.
/// </summary>
public sealed class FormattedMessageTypewriter
{
    private const string ChatEmojiTag = "chatemoji";
    private const string TypewriterHiddenAttribute = "typewriterhidden";

    private readonly FormattedMessage _source;
    private readonly FormattedMessage _boundedSource;
    private readonly TypewriterText _text;

    private FormattedMessageTypewriter(FormattedMessage source, TypewriterText text)
    {
        _source = source;
        _text = text;
        // Keep the original markup intact for ordinary messages. Rebuilding it is necessary only
        // when a long message is actually truncated; in particular, rebuilding self-closing emoji
        // tags can alter the inline-control layout.
        _boundedSource = text.IsTruncated
            ? BuildPrefix(text.SourceElementCount, appendEllipsis: false)
            : source;
    }

    public TypewriterRevealPlan RevealPlan => _text.RevealPlan;
    public int SourceElementCount => _text.SourceElementCount;
    public bool IsTruncated => _text.IsTruncated;

    public static FormattedMessageTypewriter Create(
        FormattedMessage source,
        int maximumElements,
        TimeSpan maximumDuration,
        float speedMultiplier = 1f)
        => new(source, TypewriterText.Create(
            EnumerateVisibleElements(source),
            maximumElements,
            maximumDuration,
            speedMultiplier));

    public FormattedMessage GetVisibleMessage(int visibleElementCount)
    {
        var visible = Math.Clamp(visibleElementCount, 0, RevealPlan.ElementCount);
        var sourceElementCount = Math.Min(visible, SourceElementCount);
        var appendEllipsis = IsTruncated && visible > SourceElementCount;
        return BuildPrefix(sourceElementCount, appendEllipsis);
    }

    /// <summary>
    ///     Returns the complete message with its unrevealed suffix made transparent. Unlike a plain
    ///     visible prefix, this keeps word wrapping and inline-control placement identical to the
    ///     final message for the entire typewriter animation.
    /// </summary>
    public FormattedMessage GetLayoutMessage(int visibleElementCount)
    {
        var visible = Math.Clamp(visibleElementCount, 0, RevealPlan.ElementCount);
        var remainingVisible = Math.Min(visible, SourceElementCount);
        var result = new FormattedMessage();
        var openedTags = 0;
        var skippedEmojiClosings = 0;

        // The original message can be much longer than the visible typewriter limit. Keeping its
        // discarded tail transparent would still advance layout, placing the ellipsis after text
        // that the player can never see. Layout only the bounded prefix instead.
        foreach (var node in _boundedSource)
        {
            if (node.Name == null)
            {
                if (!node.Value.TryGetString(out var text) || string.IsNullOrEmpty(text))
                    continue;

                AddTextWithTransparentSuffix(result, text, ref remainingVisible);
                continue;
            }

            if (!node.Closing && node.Name == ChatEmojiTag)
            {
                var isVisible = remainingVisible > 0;
                result.PushTag(isVisible ? node : CreateHiddenEmojiNode(node));
                result.Pop();
                skippedEmojiClosings++;
                if (isVisible)
                    remainingVisible--;

                continue;
            }

            if (node.Closing)
            {
                if (node.Name == ChatEmojiTag && skippedEmojiClosings > 0)
                {
                    skippedEmojiClosings--;
                    continue;
                }

                if (openedTags > 0)
                {
                    result.Pop();
                    openedTags--;
                }

                continue;
            }

            result.PushTag(node);
            openedTags++;
        }

        if (IsTruncated)
        {
            if (visible > SourceElementCount)
                result.AddText("…");
            else
                AddTransparentText(result, "…");
        }

        while (openedTags-- > 0)
        {
            result.Pop();
        }

        return result;
    }

    private FormattedMessage BuildPrefix(int sourceElementCount, bool appendEllipsis)
    {
        var result = new FormattedMessage();
        var remaining = sourceElementCount;
        var openedTags = 0;
        var skippedEmojiClosings = 0;

        foreach (var node in _source)
        {
            if (node.Name == null)
            {
                if (!node.Value.TryGetString(out var text) || string.IsNullOrEmpty(text))
                    continue;

                if (remaining == 0)
                    break;

                result.AddText(TakeTextElements(text, remaining, out var taken));
                remaining -= taken;
                if (remaining == 0)
                    break;

                continue;
            }

            if (!node.Closing && node.Name == ChatEmojiTag)
            {
                if (remaining == 0)
                    break;

                result.PushTag(node);
                result.Pop();
                skippedEmojiClosings++;
                remaining--;
                if (remaining == 0)
                    break;

                continue;
            }

            if (node.Closing)
            {
                if (node.Name == ChatEmojiTag && skippedEmojiClosings > 0)
                {
                    skippedEmojiClosings--;
                    continue;
                }

                if (openedTags > 0)
                {
                    result.Pop();
                    openedTags--;
                }

                continue;
            }

            result.PushTag(node);
            openedTags++;
        }

        if (appendEllipsis)
            result.AddText("…");

        while (openedTags-- > 0)
        {
            result.Pop();
        }

        return result;
    }

    private static IEnumerable<string> EnumerateVisibleElements(FormattedMessage message)
    {
        foreach (var node in message)
        {
            if (node.Name == null && node.Value.TryGetString(out var text) && !string.IsNullOrEmpty(text))
            {
                foreach (var element in TypewriterText.EnumerateTextElements(text))
                {
                    yield return element;
                }

                continue;
            }

            if (!node.Closing && node.Name == ChatEmojiTag)
                yield return "\uFFFC";
        }
    }

    private static string TakeTextElements(string text, int count, out int taken)
    {
        var builder = new StringBuilder();
        taken = 0;
        foreach (var element in TypewriterText.EnumerateTextElements(text))
        {
            if (taken >= count)
                break;

            builder.Append(element);
            taken++;
        }

        return builder.ToString();
    }

    private static void AddTextWithTransparentSuffix(
        FormattedMessage result,
        string text,
        ref int remainingVisible)
    {
        var visible = new StringBuilder();
        var hidden = new StringBuilder();
        foreach (var element in TypewriterText.EnumerateTextElements(text))
        {
            if (remainingVisible > 0)
            {
                visible.Append(element);
                remainingVisible--;
            }
            else
            {
                hidden.Append(element);
            }
        }

        if (visible.Length > 0)
            result.AddText(visible.ToString());

        if (hidden.Length > 0)
            AddTransparentText(result, hidden.ToString());
    }

    private static void AddTransparentText(FormattedMessage result, string text)
    {
        result.PushColor(Color.Transparent);
        result.AddText(text);
        result.Pop();
    }

    private static MarkupNode CreateHiddenEmojiNode(MarkupNode node)
    {
        var attributes = new Dictionary<string, MarkupParameter>(node.Attributes)
        {
            [TypewriterHiddenAttribute] = new MarkupParameter("true")
        };
        return new MarkupNode(node.Name, node.Value, attributes);
    }
}
