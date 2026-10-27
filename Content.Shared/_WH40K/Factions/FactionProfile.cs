using System.Linq;
using Content.Shared._Mono.Company;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Factions;

[Serializable, NetSerializable]
public sealed class FactionProfileSection(string title, string text)
{
    public string Title { get; } = title;
    public string Text { get; } = text;
}

/// <summary>
/// Leader-authored public information. Prototype identity and lore are deliberately absent.
/// Empty text and rank names use the standard localized presentation.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionProfile(
    int revision = 0,
    string admission = "",
    FactionProfileSection[]? sections = null,
    string leaderTitle = "",
    string officerTitle = "",
    string memberTitle = "")
{
    public const int MaxAdmissionLength = 1200;
    public const int MaxSections = 3;
    public const int MaxSectionTitleLength = 48;
    public const int MaxSectionTextLength = 2000;
    public const int MaxRankTitleLength = 32;

    public int Revision { get; } = revision;
    public string Admission { get; } = admission;
    public FactionProfileSection[] Sections { get; } = sections ?? [];
    public string LeaderTitle { get; } = leaderTitle;
    public string OfficerTitle { get; } = officerTitle;
    public string MemberTitle { get; } = memberTitle;

    public string RankTitle(FactionRole role) => role switch
    {
        FactionRole.Leader => LeaderTitle,
        FactionRole.Officer => OfficerTitle,
        _ => MemberTitle,
    };

    public static bool CanEdit(FactionRole? role) => role == FactionRole.Leader;

    public bool TryValidate(out FactionProfile normalized)
    {
        normalized = new FactionProfile();
        if (Revision < 0 || Revision == int.MaxValue ||
            !ValidText(Admission, MaxAdmissionLength, true) || Sections == null || Sections.Length > MaxSections ||
            !ValidText(LeaderTitle, MaxRankTitleLength, false) ||
            !ValidText(OfficerTitle, MaxRankTitleLength, false) ||
            !ValidText(MemberTitle, MaxRankTitleLength, false))
            return false;

        var titles = new[] { LeaderTitle.Trim(), OfficerTitle.Trim(), MemberTitle.Trim() };
        var nonEmpty = titles.Where(t => t.Length > 0).ToArray();
        if (nonEmpty.Distinct(StringComparer.OrdinalIgnoreCase).Count() != nonEmpty.Length)
            return false;

        var sections = new List<FactionProfileSection>();
        foreach (var section in Sections)
        {
            if (section == null || !ValidText(section.Title, MaxSectionTitleLength, false) ||
                !ValidText(section.Text, MaxSectionTextLength, true))
                return false;
            var title = section.Title.Trim();
            var text = section.Text.Trim();
            if ((title.Length == 0) != (text.Length == 0))
                return false;
            if (title.Length > 0)
                sections.Add(new FactionProfileSection(title, NormalizeLines(text)));
        }
        if (sections.Select(s => s.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sections.Count)
            return false;

        normalized = new FactionProfile(Revision, NormalizeLines(Admission.Trim()), sections.ToArray(), titles[0], titles[1], titles[2]);
        return true;
    }

    private static string NormalizeLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static bool ValidText(string? text, int maxLength, bool multiline) => text != null &&
        text.Length <= maxLength && !text.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t')) &&
        !text.Any(c => c is '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E' or '\u2066' or '\u2067' or '\u2068' or '\u2069');
}
