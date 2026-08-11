using Content.Shared._WH40K.ClassProgression;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Progression;

/// <summary>
/// Presentation-only RSI-state routing for the class tree.
/// Passive nodes share their specialization emblem; every active node has its own state.
/// </summary>
internal static class Wh40kClassSkillIconPaths
{
    private static readonly ResPath IconRsi = new("/Textures/_WH40K/ClassProgression/SkillIcons.rsi");

    // The Soldier tree was reauthored with domain-specific IDs while retaining the existing curated RSI art.
    // Keep this presentation-only compatibility map local to the client instead of coupling the new persistent IDs
    // to legacy prototype names or duplicating binary assets.
    private static readonly IReadOnlyDictionary<string, string> SoldierActiveStates =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["soldier-steel-curtain-05"] = "soldier-fire-line-08",
            ["soldier-steel-curtain-10"] = "soldier-fire-line-04",
            ["soldier-steel-curtain-15"] = "soldier-fire-line-05",
            ["soldier-steel-curtain-20"] = "soldier-fire-line-16",
            ["soldier-steel-curtain-25"] = "soldier-fire-line-19",
            ["soldier-void-eye-10"] = "soldier-long-shadow-10",
            ["soldier-void-eye-15"] = "soldier-long-shadow-08",
            ["soldier-void-eye-20"] = "soldier-long-shadow-04",
            ["soldier-void-eye-25"] = "soldier-long-shadow-06",
        };

    public static SpriteSpecifier.Rsi ClassSigil => new(IconRsi, "class-sigil");

    public static SpriteSpecifier.Rsi GetSpecifier(Wh40kClassSkillPrototype skill)
    {
        var state = skill.Kind == Wh40kClassSkillKind.Active
            ? SoldierActiveStates.GetValueOrDefault(skill.ID, skill.ID)
            : $"branch-{skill.Specialization.Id}";
        return new SpriteSpecifier.Rsi(IconRsi, state);
    }
}
