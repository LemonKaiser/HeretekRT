using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Content.Server.Speech;
using Content.Shared._WH40K.WolfChem.Narcotics;
using Robust.Shared.Random;

namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>
/// How a high sounds in chat from the heavy phase on: stimulants rush and stutter, opioids slur and trail off,
/// cannabinoids fill and lose the thread, empathogens stretch and soften, and psychedelics swap words around.
/// Supports both Latin and Cyrillic speech.
/// </summary>
public sealed class Wh40kDrugSpeechSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private Wh40kIntoxicationSystem _intoxication = default!;

    private static readonly Regex Token = new(@"\S+");
    private static readonly Regex FullStop = new(@"(?<=\p{L})\.(?=\s|$)");
    private static readonly Regex Ending = new(@"[.!?,;:]+$");
    private static readonly Regex Vowels = new(@"[aeiouAEIOUаеёиоуыэюяАЕЁИОУЫЭЮЯ]+(?=[^aeiouAEIOUаеёиоуыэюяАЕЁИОУЫЭЮЯ]*$)");

    private const int Fillers = 4;
    private const int Tails = 3;
    private const int Forgets = 4;
    private const int Gushes = 4;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kIntoxicationComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, Wh40kIntoxicationComponent comp, AccentGetEvent args)
    {
        var stimulant = _intoxication.FamilyPhase(comp, Wh40kDrugFamily.Stimulant);
        if (stimulant >= Wh40kDrugPhase.Heavy)
            args.Message = Rush(args.Message, stimulant == Wh40kDrugPhase.Overdose);

        var opioid = _intoxication.FamilyPhase(comp, Wh40kDrugFamily.Opioid);
        if (opioid >= Wh40kDrugPhase.Heavy)
            args.Message = Trail(Slur(args.Message, opioid == Wh40kDrugPhase.Overdose), opioid == Wh40kDrugPhase.Overdose);

        var cannabis = _intoxication.FamilyPhase(comp, Wh40kDrugFamily.Cannabinoid);
        if (cannabis >= Wh40kDrugPhase.Heavy)
            args.Message = Haze(args.Message, cannabis == Wh40kDrugPhase.Overdose);

        var empathogen = _intoxication.FamilyPhase(comp, Wh40kDrugFamily.Empathogen);
        if (empathogen >= Wh40kDrugPhase.Heavy)
            args.Message = Gush(args.Message, empathogen == Wh40kDrugPhase.Overdose);

        if (_intoxication.FamilyPhase(comp, Wh40kDrugFamily.Psychedelic) == Wh40kDrugPhase.Heavy)
            args.Message = Muddle(args.Message);
    }

    public string Rush(string message, bool overdose)
    {
        var repeat = overdose ? 0.45f : 0.25f;
        var shout = overdose ? 0.4f : 0f;
        var stutter = overdose ? 0.2f : 0.15f;
        var rushed = Token.Replace(message, match =>
        {
            var word = match.Value;
            if (overdose)
                word = word.Replace(",", string.Empty);

            if (_random.Prob(shout))
                word = word.ToUpperInvariant();

            if (word.Length > 1 && char.IsLetter(word[0]) && _random.Prob(stutter))
                word = Stutter(word, _random.Next(1, 3));

            var core = Ending.Replace(word, string.Empty);
            if (core.Length == 0 || !_random.Prob(repeat))
                return word;

            return overdose ? $"{core} {word}" : $"{core}, {word}";
        });

        return FullStop.Replace(rushed, "!");
    }

    private static string Stutter(string word, int times)
    {
        var shouted = word == word.ToUpperInvariant();
        var first = word[0];
        var echo = shouted ? first : char.ToLowerInvariant(first);
        var result = new StringBuilder();
        result.Append(first).Append('-');
        for (var i = 1; i < times; i++)
        {
            result.Append(echo).Append('-');
        }

        return result.Append(echo).Append(word, 1, word.Length - 1).ToString();
    }

    public string Slur(string message, bool overdose)
    {
        var soften = overdose ? 0.2f : 0.1f;
        var drag = overdose ? 0.08f : 0.04f;
        var result = new StringBuilder(message.Length + 8);
        foreach (var c in message)
        {
            if (_random.Prob(soften))
            {
                var swap = char.ToLowerInvariant(c) switch
                {
                    'o' => "u",
                    's' => "sh",
                    'a' => "ah",
                    'u' => "oo",
                    'c' => "k",
                    'о' => "у",
                    'с' => "ш",
                    'з' => "ж",
                    'а' => "аа",
                    _ => null,
                };

                if (swap != null)
                {
                    result.Append(char.IsUpper(c) ? char.ToUpperInvariant(swap[0]) + swap[1..] : swap);
                    continue;
                }
            }

            result.Append(c);
            if (char.IsLetter(c) && _random.Prob(drag))
                result.Append(char.ToLowerInvariant(c));
        }

        return result.ToString();
    }

    public string Trail(string message, bool overdose)
    {
        var pause = overdose ? 0.4f : 0.25f;
        var drop = overdose ? 0.12f : 0f;
        var tokens = Token.Matches(message);
        var result = new StringBuilder();
        var from = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var last = i == tokens.Count - 1;
            if (i > 0 && !last && !Ending.IsMatch(token.Value) && _random.Prob(drop))
            {
                from = token.Index + token.Length;
                continue;
            }

            result.Append(message, from, token.Index - from);
            from = token.Index + token.Length;

            if (last)
            {
                result.Append(Ending.Replace(token.Value, string.Empty)).Append("...");
                continue;
            }

            result.Append(token.Value);
            if (!Ending.IsMatch(token.Value) && _random.Prob(pause))
                result.Append("...");
        }

        result.Append(message, from, message.Length - from);
        return result.ToString();
    }

    public string Haze(string message, bool overdose)
    {
        var filler = overdose ? 0.2f : 0.1f;
        var tokens = Token.Matches(message).Select(m => m.Value).ToList();
        if (tokens.Count == 0)
            return message;

        if (overdose && tokens.Count >= 4 && _random.Prob(0.35f))
        {
            tokens.RemoveRange((tokens.Count + 1) / 2, tokens.Count / 2);
            tokens[^1] = $"{Ending.Replace(tokens[^1], string.Empty)}... {Pick("wh40k-drug-speech-forget", Forgets)}";
        }
        else if (_random.Prob(overdose ? 0.5f : 0.4f) && Ending.Replace(tokens[^1], string.Empty) is { Length: > 0 } end)
        {
            tokens[^1] = $"{end}, {Pick("wh40k-drug-speech-tail", Tails)}...";
        }

        var result = new StringBuilder(tokens[0]);
        for (var i = 1; i < tokens.Count; i++)
        {
            if (_random.Prob(filler))
                result.Append(' ').Append(Pick("wh40k-drug-speech-filler", Fillers));

            result.Append(' ').Append(tokens[i]);
        }

        return result.ToString();
    }

    public string Gush(string message, bool overdose)
    {
        var stretch = overdose ? 0.3f : 0.15f;
        var gushed = Token.Replace(message, match =>
        {
            if (!_random.Prob(stretch) || Vowels.Match(match.Value) is not { Success: true } vowel)
                return match.Value;

            var drawn = new string(vowel.Value[^1], _random.Next(2, 4));
            return match.Value.Insert(vowel.Index + vowel.Length, drawn);
        });

        gushed = FullStop.Replace(gushed, "~");
        if (overdose && _random.Prob(0.4f))
            gushed = $"{gushed} {Pick("wh40k-drug-speech-gush", Gushes)}";

        return gushed;
    }

    public string Muddle(string message)
    {
        var tokens = Token.Matches(message).Select(m => m.Value).ToArray();
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (!_random.Prob(0.15f))
                continue;

            (tokens[i], tokens[i + 1]) = (tokens[i + 1], tokens[i]);
            i++;
        }

        return string.Join(' ', tokens);
    }

    private string Pick(string prefix, int count)
    {
        return Loc.GetString($"{prefix}-{_random.Next(1, count + 1)}");
    }
}
