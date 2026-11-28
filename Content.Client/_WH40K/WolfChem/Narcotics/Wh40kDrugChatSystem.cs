using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared.Chat;
using Robust.Client.Player;

namespace Content.Client._WH40K.WolfChem.Narcotics;

/// <summary>
/// Chat seen through the local player's high, on their screen only: psychedelics colour each word and jumble a few;
/// opioids grey speech out, empathogens warm it and cannabinoids tint it green.
/// </summary>
public sealed class Wh40kDrugChatSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private Wh40kIntoxicationSystem _intoxication = default!;

    private const ChatChannel Distorted = ChatChannel.Local | ChatChannel.Whisper | ChatChannel.Radio | ChatChannel.Emotes;

    private static readonly Regex Tag = new(@"(?<!\\)\[[^\]]*\]");
    private static readonly Regex Word = new(@"\p{L}[\p{L}']*");

    private static readonly Dictionary<Wh40kDrugFamily, Color> Tints = new()
    {
        [Wh40kDrugFamily.Opioid] = Color.FromHex("#8f98ab"),
        [Wh40kDrugFamily.Empathogen] = Color.FromHex("#ffa8cc"),
        [Wh40kDrugFamily.Cannabinoid] = Color.FromHex("#b4dca0"),
    };

    private const float HueStep = 0.09f;

    public void Distort(ChatMessage msg)
    {
        if ((msg.Channel & Distorted) == 0 || string.IsNullOrEmpty(msg.Message))
            return;

        var start = msg.WrappedMessage.LastIndexOf(msg.Message, StringComparison.Ordinal);
        if (start < 0 || DistortMarkup(msg.Message, msg.WrappedMessage.GetHashCode()) is not { } body)
            return;

        msg.WrappedMessage = msg.WrappedMessage.Remove(start, msg.Message.Length).Insert(start, body);
    }

    public string? DistortMarkup(string markup, int seed)
    {
        if (_player.LocalEntity is not { } local
            || !TryComp<Wh40kIntoxicationComponent>(local, out var comp)
            || _intoxication.DominantHigh(comp) is not { } high)
            return null;

        if (high.Family == Wh40kDrugFamily.Psychedelic)
            return Kaleidoscope(markup, high, new Random(seed));

        if (Tints.TryGetValue(high.Family, out var tint))
            return $"[color={Color.InterpolateBetween(Color.White, tint, 0.35f + 0.65f * high.Strength).ToHex()}]{markup}[/color]";

        return null;
    }

    private static string Kaleidoscope(string text, Wh40kDominantHigh high, Random random)
    {
        var hue = random.NextSingle();
        var saturation = 0.2f + 0.5f * high.Strength;
        var jumble = high.Phase switch
        {
            Wh40kDrugPhase.Heavy => 0.06f,
            Wh40kDrugPhase.Overdose => 0.15f,
            _ => 0f,
        };

        var result = new StringBuilder();
        var last = 0;
        foreach (Match tag in Tag.Matches(text))
        {
            Colour(text.Substring(last, tag.Index - last));
            result.Append(tag.Value);
            last = tag.Index + tag.Length;
        }

        Colour(text.Substring(last));
        return result.ToString();

        void Colour(string plain)
        {
            var from = 0;
            foreach (Match word in Word.Matches(plain))
            {
                result.Append(plain, from, word.Index - from);
                var shown = word.Length >= 4 && random.NextSingle() < jumble ? Shuffle(word.Value, random) : word.Value;
                var color = Color.FromHsv(new Vector4(hue, saturation, 1f, 1f));
                result.Append($"[color={color.ToHex()}]{shown}[/color]");
                hue = (hue + HueStep) % 1f;
                from = word.Index + word.Length;
            }

            result.Append(plain, from, plain.Length - from);
        }
    }

    private static string Shuffle(string word, Random random)
    {
        var letters = word.ToCharArray();
        for (var i = letters.Length - 2; i > 1; i--)
        {
            var j = random.Next(1, i + 1);
            (letters[i], letters[j]) = (letters[j], letters[i]);
        }

        return new string(letters);
    }
}
