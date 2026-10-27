using System.Linq;
using Content.Shared._WH40K.Factions;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Factions;

/// <summary>Shared disclosure sections for the public profile and the member overview.</summary>
public sealed class FactionProfileSections : BoxContainer
{
    private readonly RichTextLabel _admission;
    private readonly RichTextLabel _ranks;
    private readonly BoxContainer _extra;
    private FactionProfile? _profile;

    public FactionProfileSections()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 10;
        HorizontalExpand = true;
        _admission = AddDisclosure(this, Loc.GetString("wh40k-faction-joining-help-toggle"));
        _ranks = AddDisclosure(this, Loc.GetString("wh40k-faction-ranks-help-toggle"));
        _extra = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 10 };
        AddChild(_extra);
        UpdateProfile(new FactionProfile());
    }

    public void UpdateProfile(FactionProfile profile)
    {
        if (ReferenceEquals(_profile, profile))
            return;
        var sectionsChanged = _profile == null || !_profile.Sections.Select(s => (s.Title, s.Text))
            .SequenceEqual(profile.Sections.Select(s => (s.Title, s.Text)));
        _profile = profile;
        _admission.SetMessage(FormattedMessage.FromUnformatted(string.IsNullOrEmpty(profile.Admission)
            ? Loc.GetString("wh40k-faction-join-steps") : profile.Admission), FactionUiPresentation.Text);
        _ranks.SetMessage(FormattedMessage.FromUnformatted(FactionUiPresentation.Ranks(profile)), FactionUiPresentation.Text);
        if (!sectionsChanged)
            return;
        _extra.RemoveAllChildren();
        foreach (var section in profile.Sections)
            AddDisclosure(_extra, section.Title).SetMessage(FormattedMessage.FromUnformatted(section.Text), FactionUiPresentation.Text);
    }

    private static RichTextLabel AddDisclosure(Control parent, string title)
    {
        var group = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
        var toggle = new LobbyFactionButton
        {
            Text = title, ToggleMode = true, ToolTip = title, ClipText = true, TextAlign = Label.AlignMode.Left,
        };
        var text = new RichTextLabel { HorizontalExpand = true, Visible = false, LineHeightScale = 1.2f, Margin = new Thickness(12, 0, 8, 6) };
        text.AddStyleClass("Wh40kOnboardingBodyCompact");
        toggle.OnToggled += args => text.Visible = args.Pressed;
        group.AddChild(toggle);
        group.AddChild(text);
        parent.AddChild(group);
        return text;
    }
}
