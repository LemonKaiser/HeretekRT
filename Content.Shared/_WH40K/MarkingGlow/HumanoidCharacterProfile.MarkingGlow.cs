using System.Linq;
using Content.Shared._WH40K.MarkingGlow;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    [DataField]
    public List<string> MarkingGlow { get; set; } = new();

    public HumanoidCharacterProfile WithMarkingGlow(string entry, bool on)
    {
        return new(this) { MarkingGlow = Wh40kMarkingGlowRules.Set(MarkingGlow, entry, on) };
    }

    public HumanoidCharacterProfile WithMarkingGlowList(IEnumerable<string>? entries)
    {
        return new(this) { MarkingGlow = Wh40kMarkingGlowRules.Clean(entries) };
    }

    public bool Glows(string entry)
    {
        return MarkingGlow.Contains(entry);
    }

    private bool MarkingGlowEqual(HumanoidCharacterProfile other)
    {
        return MarkingGlow.SequenceEqual(other.MarkingGlow);
    }

    private void EnsureMarkingGlowValid()
    {
        MarkingGlow = Wh40kMarkingGlowRules.Clean(MarkingGlow, Appearance.Markings);
    }
}
