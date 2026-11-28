using System.Linq;
using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.MedicalScanner;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.HealthAnalyzer.UI;

/// <summary>
/// WH40K Heritage-styled chemical &amp; narcotic conditions readout appended to the Health Analyzer diagnostics scroll.
/// </summary>
public sealed partial class HealthAnalyzerWindow
{
    private static readonly Color ConditionHeadingColor = Color.FromHex("#E1C17E");
    private static readonly Color ConditionBodyColor = Color.FromHex("#E8E2D6");
    private static readonly Color ConditionWarningColor = Color.FromHex("#ff9100");
    private static readonly Color ConditionDangerColor = Color.FromHex("#ff4d4d");
    private static readonly Color ConditionHintColor = Color.FromHex("#B7AD9B");

    private void DrawWh40kConditions(HealthAnalyzerScannedUserMessage msg)
    {
        if (msg.ScanMode != true || msg.Conditions is not { } conditions)
            return;

        if (conditions.Drugs.Count == 0 && conditions.Held.Count == 0 && conditions.Crashes.Count == 0
            && conditions.Habits.Count == 0)
            return;

        var section = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(0, 8, 0, 0),
            SeparationOverride = 2,
        };

        section.AddChild(new PanelContainer
        {
            StyleClasses = { "LowDivider" },
            Margin = new Thickness(0, 0, 0, 4),
        });

        if (conditions.Drugs.Count > 0)
        {
            AddConditionHeading(section, "wh40k-analyzer-drugs-title", first: true);
            foreach (var drug in conditions.Drugs)
            {
                AddConditionLine(section, Loc.GetString("wh40k-analyzer-drug",
                        ("family", CapitalizeFirst(FamilyName(drug.Family))),
                        ("phase", Loc.GetString($"wh40k-analyzer-phase-{drug.Phase.ToString().ToLowerInvariant()}"))),
                    drug.Phase == Wh40kDrugPhase.Overdose ? ConditionDangerColor : ConditionWarningColor);

                if (drug.Causes.Count == 0)
                    AddConditionLine(section, Loc.GetString("wh40k-analyzer-drug-no-cause"), ConditionHintColor, 1);

                foreach (var cause in drug.Causes)
                    AddConditionCause(section, cause);

                if (drug.Antidotes.Count > 0)
                    AddConditionLine(section, Loc.GetString("wh40k-analyzer-antidote", ("reagents", ReagentNames(drug.Antidotes))), ConditionHintColor, 1);
            }
        }

        if (conditions.Held.Count > 0)
        {
            AddConditionHeading(section, "wh40k-analyzer-held-title", first: section.ChildCount <= 1);
            foreach (var held in conditions.Held.OrderByDescending(held => held.SecondsLeft))
            {
                var name = Loc.TryGetString($"reagent-effect-status-effect-{held.Key}", out var effect) ? effect : held.Key;
                AddConditionLine(section, Loc.GetString("wh40k-analyzer-held",
                    ("effect", CapitalizeFirst(name)),
                    ("time", ConditionTime(held.SecondsLeft))), ConditionBodyColor);

                foreach (var cause in held.Causes)
                    AddConditionCause(section, cause);

                foreach (var source in held.Sources)
                {
                    AddConditionLine(section, Loc.GetString($"wh40k-analyzer-source-{source.Kind.ToString().ToLowerInvariant()}",
                        ("family", FamilyName(source.Family))), ConditionHintColor, 1);
                }

                if (held.Causes.Count == 0 && held.Sources.Count == 0)
                    AddConditionLine(section, Loc.GetString("wh40k-analyzer-held-fading"), ConditionHintColor, 1);
            }
        }

        if (conditions.Crashes.Count > 0)
        {
            AddConditionHeading(section, "wh40k-analyzer-crashes-title", first: section.ChildCount <= 1);
            foreach (var crash in conditions.Crashes)
            {
                AddConditionLine(section, Loc.GetString("wh40k-analyzer-crash",
                    ("family", FamilyName(crash.Family)),
                    ("time", ConditionTime(crash.SecondsLeft))), ConditionWarningColor);
                AddConditionLine(section, Loc.GetString("wh40k-analyzer-crash-hint"), ConditionHintColor, 1);
            }
        }

        if (conditions.Habits.Count > 0)
        {
            AddConditionHeading(section, "wh40k-analyzer-habits-title", first: section.ChildCount <= 1);
            foreach (var habit in conditions.Habits)
            {
                var family = FamilyName(habit.Family);
                AddConditionLine(section, Loc.GetString("wh40k-analyzer-habit",
                        ("family", family),
                        ("meter", Percent(habit.Meter))),
                    habit.Stage > 0 ? ConditionWarningColor : ConditionBodyColor);

                if (habit.Stage > 0)
                {
                    AddConditionLine(section, Loc.GetString("wh40k-analyzer-withdrawal",
                        ("stage", habit.Stage),
                        ("family", family)), ConditionDangerColor, 1);
                }
                else if (habit.WithdrawalIn >= 0f)
                {
                    AddConditionLine(section, Loc.GetString("wh40k-analyzer-withdrawal-in",
                        ("time", ConditionTime(habit.WithdrawalIn)),
                        ("family", family)), ConditionWarningColor, 1);
                }

                if (float.IsPositiveInfinity(habit.CleanIn))
                    AddConditionLine(section, Loc.GetString("wh40k-analyzer-clean-never", ("floor", Percent(habit.Floor))), ConditionHintColor, 1);
                else if (habit.CleanIn >= 0f)
                    AddConditionLine(section, Loc.GetString("wh40k-analyzer-clean-in", ("time", ConditionTime(habit.CleanIn))), ConditionHintColor, 1);

                if (habit.Tolerance > 0.01f)
                {
                    AddConditionLine(section, Loc.GetString("wh40k-analyzer-tolerance",
                        ("tolerance", Percent(habit.Tolerance)),
                        ("weaker", Percent(1f - 1f / (1f + habit.Tolerance)))), ConditionHintColor, 1);
                }
            }
        }

        GroupsContainer.AddChild(section);
    }

    private void AddConditionCause(BoxContainer section, Wh40kAnalyzerCause cause)
    {
        var name = ReagentNames(new List<string> { cause.Reagent });
        var text = cause.Stomach
            ? Loc.GetString("wh40k-analyzer-cause-stomach", ("reagent", name), ("quantity", cause.Quantity))
            : cause.ClearSeconds >= 0f
                ? Loc.GetString("wh40k-analyzer-cause", ("reagent", name), ("quantity", cause.Quantity),
                    ("time", ConditionTime(cause.ClearSeconds)))
                : Loc.GetString("wh40k-analyzer-cause-stalled", ("reagent", name), ("quantity", cause.Quantity));

        AddConditionLine(section, text, ConditionBodyColor, 1);
        if (cause.RemovedBy.Count > 0)
            AddConditionLine(section, Loc.GetString("wh40k-analyzer-removed-by", ("reagents", ReagentNames(cause.RemovedBy))), ConditionHintColor, 2);
    }

    private static void AddConditionHeading(BoxContainer section, string loc, bool first)
    {
        section.AddChild(new Label
        {
            Text = Loc.GetString(loc),
            FontColorOverride = ConditionHeadingColor,
            Margin = new Thickness(0, first ? 0 : 6, 0, 2),
        });
    }

    private static void AddConditionLine(BoxContainer section, string text, Color color, int indent = 0)
    {
        var message = new FormattedMessage();
        message.PushColor(color);
        message.AddText(text);
        message.Pop();

        var label = new RichTextLabel
        {
            HorizontalExpand = true,
            MaxWidth = Math.Max(180, 285 - indent * 10),
            Margin = new Thickness(indent * 10, 1, 12, 1),
        };
        label.SetMessage(message);
        section.AddChild(label);
    }

    private string ReagentNames(List<string> reagents)
    {
        return string.Join(", ", reagents.Select(id =>
            _prototypes.TryIndex(id, out ReagentPrototype? reagent) ? reagent.LocalizedName : id));
    }

    private static string FamilyName(Wh40kDrugFamily family)
    {
        return Loc.GetString($"wh40k-drug-family-{family.ToString().ToLowerInvariant()}");
    }

    private static int Percent(float share)
    {
        return (int) MathF.Round(share * 100f);
    }

    private static string ConditionTime(float seconds)
    {
        var total = (int) MathF.Ceiling(Math.Max(0f, seconds));
        return Loc.GetString("wh40k-analyzer-time", ("minutes", total / 60), ("seconds", (total % 60).ToString("00")));
    }

    private static string CapitalizeFirst(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value[..1].ToUpper() + value[1..];
    }
}
