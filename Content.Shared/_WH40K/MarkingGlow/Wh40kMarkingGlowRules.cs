using System.Linq;
using Content.Shared.Humanoid.Markings;

namespace Content.Shared._WH40K.MarkingGlow;

/// <summary>
/// Rules for glowing character markings, eyes, hair, and facial hair.
/// Special keys start with '@' so they never collide with marking prototype IDs.
/// </summary>
public static class Wh40kMarkingGlowRules
{
    public const string Eyes = "@eyes";
    public const string Hair = "@hair";
    public const string FacialHair = "@facial_hair";

    public const int MaxEntries = 64;

    private const char Separator = ';';

    public static List<string> Clean(IEnumerable<string>? glow, IEnumerable<Marking>? worn = null)
    {
        if (glow == null)
            return new List<string>();

        var ids = worn?.Select(marking => marking.MarkingId).ToHashSet();
        return glow
            .Where(entry => !string.IsNullOrEmpty(entry) && !entry.Contains(Separator))
            .Where(entry => IsKey(entry) || ids == null || ids.Contains(entry))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Take(MaxEntries)
            .ToList();
    }

    public static bool IsKey(string entry)
    {
        return entry is Eyes or Hair or FacialHair;
    }

    public static bool Glows(IReadOnlyCollection<string> glow, MarkingPrototype marking)
    {
        if (glow.Count == 0)
            return false;

        return glow.Contains(marking.ID)
            || (marking.MarkingCategory == MarkingCategories.Hair && glow.Contains(Hair))
            || (marking.MarkingCategory == MarkingCategories.FacialHair && glow.Contains(FacialHair));
    }

    public static List<string> Set(IEnumerable<string> glow, string entry, bool on)
    {
        var set = glow.ToHashSet();
        if (on)
            set.Add(entry);
        else
            set.Remove(entry);

        return Clean(set);
    }

    public static string ToStored(IEnumerable<string> glow)
    {
        return string.Join(Separator, Clean(glow));
    }

    public static List<string> FromStored(string? stored)
    {
        return string.IsNullOrEmpty(stored) ? new List<string>() : Clean(stored.Split(Separator));
    }
}
