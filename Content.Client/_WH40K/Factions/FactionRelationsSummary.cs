using System.Linq;
using System.Numerics;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Factions;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Factions;

public sealed class FactionRelationsSummary : BoxContainer
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    public FactionRelationsSummary()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = 10;
        Margin = new Thickness(0, 12, 0, 0);
    }

    public void UpdateRelations(string source, FactionRelation[] relations)
    {
        RemoveAllChildren();
        AddChild(FactionSocialUi.Body(Loc.GetString("wh40k-faction-rel-profile-help"), true));
        var count = 0;
        foreach (var target in _prototypes.EnumeratePrototypes<CompanyPrototype>()
                     .Where(f => !f.Disabled && f.ID != "None" && f.ID != source).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var relation = relations.FirstOrDefault(r => r.Source == source && r.Target == target.ID || r.Target == source && r.Source == target.ID)
                ?? new FactionRelation(source, target.ID);
            var card = FactionSocialUi.Card();
            card.Name = "Relation-" + target.ID;
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 14 };
            var emblem = new TextureRect { SetSize = new Vector2(56), CanShrink = true, Stretch = TextureRect.StretchMode.KeepAspectCentered, VerticalAlignment = VAlignment.Center };
            if (target.Image is { } image)
                emblem.TexturePath = image;
            row.AddChild(emblem);
            var description = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 5 };
            description.AddChild(FactionSocialUi.Heading(target.Name, 15));
            description.AddChild(new Label
            {
                Name = "RelationStatus", Text = $"{FactionRelationsPanel.Signed(relation.Score)} · {Loc.GetString(FactionSocialRules.StatusKey(relation.Score))}",
                FontColorOverride = FactionRelationsPanel.RelationColor(relation.Score), ClipText = true,
            });
            description.AddChild(new FactionRelationGauge { Score = relation.Score });
            if (!string.IsNullOrWhiteSpace(relation.Reason))
                description.AddChild(FactionSocialUi.Body(relation.Reason, true));
            row.AddChild(description);
            card.AddChild(row);
            AddChild(card);
            count++;
        }
        if (count == 0)
            AddChild(FactionSocialUi.Body(Loc.GetString("wh40k-faction-rel-profile-empty"), true));
    }
}
