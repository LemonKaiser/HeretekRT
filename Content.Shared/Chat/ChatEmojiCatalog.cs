using System.Collections.Frozen;
using System.Linq;
using System.Text;
using Content.Shared.Chat.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.Chat;

/// <summary>
/// Cached content-defined emoji catalogue shared by client and server.
/// A shortcode accepted by the server therefore always has a known client-side representation.
/// </summary>
public sealed partial class ChatEmojiCatalog
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;

    private static readonly ChatEmojiCategory[] CategoryOrder =
    [
        ChatEmojiCategory.Custom,
        ChatEmojiCategory.Smileys,
        ChatEmojiCategory.Nature,
        ChatEmojiCategory.Food,
        ChatEmojiCategory.Activities,
        ChatEmojiCategory.Travel,
        ChatEmojiCategory.Objects,
        ChatEmojiCategory.Symbols,
        ChatEmojiCategory.Flags,
    ];

    private FrozenDictionary<string, ChatEmojiDefinition> _aliases = FrozenDictionary<string, ChatEmojiDefinition>.Empty;
    private FrozenDictionary<ChatEmojiCategory, ChatEmojiDefinition[]> _byCategory = FrozenDictionary<ChatEmojiCategory, ChatEmojiDefinition[]>.Empty;
    private FrozenDictionary<char, DirectEmojiMatch[]> _directByFirstChar = FrozenDictionary<char, DirectEmojiMatch[]>.Empty;
    private ChatEmojiDefinition[] _allEmojis = [];
    private ChatEmojiDefinition? _customCategoryIcon;

    public event Action? Changed;

    public void Initialize()
    {
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
        Rebuild();
    }

    public bool TryGet(string alias, out ChatEmojiDefinition definition)
        => _aliases.TryGetValue(alias, out definition);

    public IReadOnlyList<ChatEmojiDefinition> GetCategory(ChatEmojiCategory category)
        => _byCategory.GetValueOrDefault(category, []);

    /// <summary>
    /// Returns one emoji from the complete catalogue. Alternative aliases do not affect the selection chance.
    /// </summary>
    public ChatEmojiDefinition GetRandomEmoji()
        => _allEmojis.Length > 0
            ? _random.Pick(_allEmojis)
            : GetCategoryIcon(ChatEmojiCategory.Custom);

    /// <summary>
    /// Returns a random emoji different from <paramref name="previous"/> whenever the catalogue contains a choice.
    /// </summary>
    public ChatEmojiDefinition GetRandomEmoji(ChatEmojiDefinition? previous)
    {
        var emoji = GetRandomEmoji();
        if (previous is not { } prior || _allEmojis.Length < 2 || emoji != prior)
            return emoji;

        // A few retries keep the simple random distribution while guaranteeing a visible change in normal catalogues.
        for (var attempt = 0; attempt < 3 && emoji == prior; attempt++)
            emoji = GetRandomEmoji();

        if (emoji != prior)
            return emoji;

        return _allEmojis.First(candidate => candidate != prior);
    }

    public IEnumerable<ChatEmojiCategory> GetCategoryOrder()
    {
        foreach (var category in CategoryOrder)
        {
            if (_byCategory.ContainsKey(category))
                yield return category;
        }
    }

    public ChatEmojiDefinition GetCategoryIcon(ChatEmojiCategory category)
    {
        if (category == ChatEmojiCategory.Custom)
        {
            if (_customCategoryIcon is { } cached)
                return cached;

            var customEmojis = GetCategory(ChatEmojiCategory.Custom);
            if (customEmojis.Count > 0)
                return (_customCategoryIcon = _random.Pick(customEmojis)).Value;

            if (TryGet("question", out var fallback))
                return fallback;
        }

        if (TryGet(ChatEmoji.GetCategoryIconAlias(category), out var icon))
            return icon;

        var categoryItems = GetCategory(category);
        if (categoryItems.Count > 0)
            return categoryItems[0];

        return GetCategory(ChatEmojiCategory.Smileys).FirstOrDefault();
    }

    public IEnumerable<ChatEmojiDefinition> Search(ChatEmojiCategory category, string? query)
    {
        var items = GetCategory(category);
        if (string.IsNullOrWhiteSpace(query))
            return items;

        var normalized = query.Trim();
        return items.Where(emoji => MatchesSearch(emoji, normalized));
    }

    /// <summary>
    /// Matches direct Unicode input. Both presentation variants are accepted. A Fitzpatrick modifier is consumed
    /// with the base emoji and deliberately resolves to the neutral sprite without creating colour variants.
    /// </summary>
    public bool TryMatchDirectEmoji(string text, int index, out ChatEmojiDefinition emoji, out int consumedLength)
    {
        emoji = default;
        consumedLength = 0;
        if (index < 0 || index >= text.Length || !_directByFirstChar.TryGetValue(text[index], out var matches))
            return false;

        foreach (var match in matches)
        {
            if (match.Value.Length > text.Length - index ||
                string.CompareOrdinal(text, index, match.Value, 0, match.Value.Length) != 0)
                continue;

            emoji = match.Emoji;
            consumedLength = match.Value.Length;
            if (TryGetSkinToneLength(text, index + consumedLength, out var skinToneLength))
                consumedLength += skinToneLength;
            return true;
        }

        return false;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<ChatEmojiPackPrototype>() || args.WasModified<ChatCustomEmojiPrototype>())
            Rebuild();
    }

    private void Rebuild()
    {
        _customCategoryIcon = null;
        var aliases = new Dictionary<string, ChatEmojiDefinition>(StringComparer.OrdinalIgnoreCase);
        var categories = new Dictionary<ChatEmojiCategory, List<ChatEmojiDefinition>>();
        var direct = new Dictionary<char, List<DirectEmojiMatch>>();
        var allEmojis = new List<ChatEmojiDefinition>();

        foreach (var pack in _prototypes.EnumeratePrototypes<ChatEmojiPackPrototype>()
                     .OrderBy(proto => proto.Order)
                     .ThenBy(proto => proto.ID, StringComparer.Ordinal))
        {
            foreach (var token in pack.Definitions.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = token.IndexOf('=');
                if (separator <= 0 || separator == token.Length - 1)
                {
                    Logger.ErrorS("chat.emoji", $"Malformed emoji entry '{token}' in pack '{pack.ID}'.");
                    continue;
                }

                var alias = token[..separator].ToLowerInvariant();
                if (!ChatEmoji.IsValidAlias(alias))
                {
                    Logger.ErrorS("chat.emoji", $"Invalid emoji alias '{alias}' in pack '{pack.ID}'.");
                    continue;
                }

                ChatEmojiDefinition definition;
                try
                {
                    definition = new ChatEmojiDefinition(alias, ChatEmoji.DecodeValue(token[(separator + 1)..]), pack.Category);
                }
                catch (Exception error)
                {
                    Logger.ErrorS("chat.emoji", $"Invalid Unicode value for emoji '{alias}' in pack '{pack.ID}': {error.Message}");
                    continue;
                }

                Add(definition, [alias]);
            }
        }

        foreach (var custom in _prototypes.EnumeratePrototypes<ChatCustomEmojiPrototype>()
                     .OrderBy(proto => proto.Order)
                     .ThenBy(proto => proto.ID, StringComparer.OrdinalIgnoreCase))
        {
            var definition = custom.ToDefinition();
            var aliasesForDefinition = new List<string> { definition.Alias };
            aliasesForDefinition.AddRange(custom.Aliases);
            Add(definition, aliasesForDefinition);
        }

        _aliases = aliases.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _byCategory = categories.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.OrderBy(emoji => emoji.Alias, StringComparer.OrdinalIgnoreCase).ToArray())
            .ToFrozenDictionary();
        _allEmojis = allEmojis.ToArray();
        _directByFirstChar = direct.ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .GroupBy(match => match.Value, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderByDescending(match => match.Value.Length)
                    .ThenBy(match => match.Emoji.Alias, StringComparer.Ordinal)
                    .ToArray())
            .ToFrozenDictionary();
        Changed?.Invoke();

        void Add(ChatEmojiDefinition definition, IEnumerable<string> aliasesForDefinition)
        {
            var added = false;
            foreach (var suppliedAlias in aliasesForDefinition)
            {
                var alias = suppliedAlias.ToLowerInvariant();
                if (!ChatEmoji.IsValidAlias(alias))
                {
                    Logger.ErrorS("chat.emoji", $"Invalid emoji alias '{suppliedAlias}' for '{definition.Alias}'.");
                    continue;
                }

                if (!aliases.TryAdd(alias, definition))
                {
                    Logger.ErrorS("chat.emoji", $"Duplicate emoji alias '{alias}'. The first definition is kept.");
                    continue;
                }

                added = true;
            }

            if (!added)
                return;

            allEmojis.Add(definition);
            if (!categories.TryGetValue(definition.Category, out var category))
                categories[definition.Category] = category = new List<ChatEmojiDefinition>();
            category.Add(definition);

            if (!definition.HasDirectValue)
                return;

            AddDirectPattern(definition.Value, definition);
            var withoutVariationSelectors = StripVariationSelectors(definition.Value);
            if (!string.Equals(definition.Value, withoutVariationSelectors, StringComparison.Ordinal))
                AddDirectPattern(withoutVariationSelectors, definition);
        }

        void AddDirectPattern(string value, ChatEmojiDefinition definition)
        {
            if (string.IsNullOrEmpty(value))
                return;

            if (!direct.TryGetValue(value[0], out var patterns))
                direct[value[0]] = patterns = new List<DirectEmojiMatch>();
            patterns.Add(new DirectEmojiMatch(value, definition));
        }
    }

    private static bool MatchesSearch(ChatEmojiDefinition emoji, string query)
    {
        if (emoji.Alias.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            emoji.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return emoji.Keywords?.Any(keyword => keyword.Contains(query, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string StripVariationSelectors(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value is < 0xFE00 or > 0xFE0F)
                builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    private static bool TryGetSkinToneLength(string value, int index, out int length)
    {
        length = 0;
        if (index >= value.Length)
            return false;

        var codePoint = char.ConvertToUtf32(value, index);
        if (codePoint is < 0x1F3FB or > 0x1F3FF)
            return false;

        length = char.IsSurrogatePair(value, index) ? 2 : 1;
        return true;
    }

    private readonly record struct DirectEmojiMatch(string Value, ChatEmojiDefinition Emoji);
}
