using Content.Shared.CrewManifest;
using Content.Shared.StatusIcon;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using System.Numerics;
using Content.Shared.Roles;
using Content.Client._WH40K.UserInterface;

namespace Content.Client.CrewManifest.UI;

public sealed class CrewManifestSection : BoxContainer
{
    public CrewManifestSection(
        IPrototypeManager prototypeManager,
        SpriteSystem spriteSystem,
        DepartmentPrototype section,
        List<CrewManifestEntry> entries)
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;

        AddChild(new Label()
        {
            StyleClasses = { "LabelHeading" },
            Text = Loc.GetString(section.Name)
        });

        var listing = new BoxContainer()
        {
            HorizontalExpand = true,
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 4,
        };

        AddChild(listing);

        foreach (var entry in entries)
        {
            var name = new RichTextLabel()
            {
                HorizontalExpand = true,
            };
            name.SetMessage(entry.Name, Wh40kInterfaceTheme.Text);

            var titleContainer = new BoxContainer()
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true
            };

            var title = new RichTextLabel();
            title.SetMessage(entry.JobTitle, Color.FromHex("#ADA391"));


            if (prototypeManager.TryIndex<JobIconPrototype>(entry.JobIcon, out var jobIcon))
            {
                var icon = new TextureRect()
                {
                    TextureScale = Vector2.One,
                    VerticalAlignment = VAlignment.Center,
                    Texture = spriteSystem.Frame0(jobIcon.Icon),
                    Margin = new Thickness(0, 0, 4, 0)
                };

                titleContainer.AddChild(icon);
                titleContainer.AddChild(title);
            }
            else
            {
                titleContainer.AddChild(title);
            }

            var row = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };
            row.AddChild(name);
            row.AddChild(titleContainer);
            var panel = new PanelContainer
            {
                HorizontalExpand = true,
                PanelOverride = Wh40kInterfaceTheme.Surface("#100D0AEB", "#79603D80", 8, 6, 3),
            };
            panel.AddChild(row);
            listing.AddChild(panel);
        }
    }
}
