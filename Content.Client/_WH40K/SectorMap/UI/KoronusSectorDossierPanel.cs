using System.Linq;
using Content.Shared._WH40K.Activities;
using Content.Shared._WH40K.SectorMap;
using Content.Shared._WH40K.SectorMap.BUI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.SectorMap.UI;

public sealed class KoronusSectorDossierPanel : PanelContainer
{
    private readonly BoxContainer _content = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10 };
    private readonly Label _title = new() { FontColorOverride = Color.FromHex("#E4CE98"), ClipText = true };
    private readonly RichTextLabel _navigation = new();
    private readonly BoxContainer _activities = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10 };
    private readonly Button _jump = new();
    private string _signature = string.Empty;
    private string? _destination;

    public event Action<string>? JumpRequested;
    public event Action<string>? SystemSelected;

    public KoronusSectorDossierPanel()
    {
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#111510").WithAlpha(0.97f),
            BorderColor = Color.FromHex("#8A7956"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 14,
            ContentMarginRightOverride = 14,
            ContentMarginTopOverride = 12,
            ContentMarginBottomOverride = 12,
        };
        AddChild(_content);
        _content.AddChild(_title);
        _content.AddChild(_navigation);
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true };
        scroll.AddChild(_activities);
        _content.AddChild(scroll);
        _content.AddChild(_jump);
        _jump.OnPressed += _ =>
        {
            if (_destination != null && !_jump.Disabled)
                JumpRequested?.Invoke(_destination);
        };
    }

    public void UpdateState(KoronusSectorInterfaceState state, string? selectedSystem, TimeSpan now)
    {
        Visible = state.Available;
        if (!Visible)
            return;
        var selected = state.Systems.FirstOrDefault(system => system.Id == selectedSystem);
        var signature = $"{selectedSystem}:{state.CurrentSystem}:{state.CanJump}:{state.WarpTravel?.Incident}:" +
                        string.Join(';', state.Routes.Select(route => $"{route.From}:{route.To}:{route.RouteClass}:{route.TravelTimeMultiplier}")) +
                        string.Join(';', state.ActivityDossiers.Select(activity =>
                            $"{activity.InstanceId}:{activity.State}:{Math.Ceiling((activity.ExpiresAt - now).TotalMinutes)}:{activity.LocalSignalOffset}"));
        if (_signature == signature)
            return;
        _signature = signature;

        _title.Text = selected?.Name ?? Loc.GetString("koronus-sector-dossier-overview");
        _destination = selected?.Id;
        _jump.Visible = selected != null && state.WarpTravel == null;
        _jump.Disabled = selected == null || !selected.Reachable || !state.CanJump;
        _jump.Text = Loc.GetString(_jump.Disabled ? "koronus-sector-card-warp-unavailable" : "koronus-sector-card-warp");

        var route = selected == null ? null : state.Routes.FirstOrDefault(candidate => candidate.Enabled &&
            ((candidate.From == state.CurrentSystem && candidate.To == selected.Id) ||
             candidate.Bidirectional && candidate.To == state.CurrentSystem && candidate.From == selected.Id));
        string navigation;
        if (state.WarpTravel is { } travel)
        {
            navigation = Loc.GetString("koronus-sector-dossier-travelling");
            if (travel.Incident != KoronusWarpIncident.None)
                navigation += "\n" + Loc.GetString($"koronus-warp-incident-{travel.Incident.ToString().ToLowerInvariant()}");
        }
        else if (selected == null)
            navigation = Loc.GetString("koronus-sector-dossier-help");
        else if (selected.Current)
            navigation = Loc.GetString("koronus-sector-card-current");
        else if (!selected.Enabled)
            navigation = Loc.GetString("koronus-sector-dossier-unexplored");
        else if (route == null)
            navigation = Loc.GetString("koronus-sector-card-no-route");
        else
        {
            var risk = KoronusWarpRoutePolicy.GetRisk(route.RouteClass);
            navigation = Loc.GetString("koronus-sector-dossier-route",
                ("risk", Loc.GetString($"koronus-sector-risk-{risk.ToString().ToLowerInvariant()}")),
                ("chance", selected.Id == "Footfall" ? 0 : (int) (KoronusWarpRoutePolicy.GetIncidentChance(risk) * 100)),
                ("multiplier", route.TravelTimeMultiplier.ToString("0.00")));
            navigation += "\n" + Loc.GetString(selected.Id == "Footfall" ? "koronus-sector-dossier-footfall" :
                $"koronus-sector-risk-{risk.ToString().ToLowerInvariant()}-detail");
            if (!state.CanJump)
                navigation += "\n" + Loc.GetString("koronus-sector-card-drive-offline");
        }
        _navigation.SetMessage(navigation);

        _activities.RemoveAllChildren();
        var activities = state.ActivityDossiers.Where(activity =>
                activity.State is KoronusActivityState.Available or KoronusActivityState.Engaged &&
                (selected == null || activity.SystemId == selected.Id ||
                 activity.RouteFromSystem == selected.Id || activity.RouteToSystem == selected.Id))
            .OrderByDescending(activity => KoronusActivityRuntimePolicy.IsPhysicalExecution(activity.Execution))
            .ThenByDescending(activity => activity.Risk).ThenBy(activity => activity.ExpiresAt).ToArray();
        AddText(Loc.GetString("koronus-sector-dossier-activities", ("count", activities.Length)));
        if (activities.Length == 0)
            AddText(Loc.GetString("koronus-sector-dossier-empty"));
        foreach (var activity in activities)
        {
            var entry = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
            var name = new RichTextLabel();
            name.SetMessage(Loc.GetString(activity.TitleLocId));
            entry.AddChild(name);
            var systemName = state.Systems.FirstOrDefault(system => system.Id == activity.SystemId)?.Name ?? activity.SystemId;
            var physical = KoronusActivityRuntimePolicy.IsPhysicalExecution(activity.Execution);
            var status = Loc.GetString(physical ? "koronus-sector-dossier-task" : "koronus-sector-dossier-intel",
                ("system", systemName), ("minutes", Math.Max(0, (int) Math.Ceiling((activity.ExpiresAt - now).TotalMinutes))));
            if (activity.RouteFromSystem != null && activity.RouteToSystem != null)
                status = Loc.GetString("koronus-sector-dossier-route-signal",
                    ("origin", state.Systems.FirstOrDefault(system => system.Id == activity.RouteFromSystem)?.Name ?? activity.RouteFromSystem),
                    ("destination", state.Systems.FirstOrDefault(system => system.Id == activity.RouteToSystem)?.Name ?? activity.RouteToSystem),
                    ("minutes", Math.Max(0, (int) Math.Ceiling((activity.ExpiresAt - now).TotalMinutes))));
            if (physical)
                status += "\n" + Loc.GetString("koronus-sector-dossier-participants", ("count", activity.MinimumParticipants));
            if (activity.LocalSignalOffset is { } offset)
                status += "\n" + Loc.GetString("koronus-sector-dossier-local-signal",
                    ("distance", (int) MathF.Round(offset.Length())),
                    ("direction", Loc.GetString($"koronus-direction-{offset.ToWorldAngle().GetDir().ToString().ToLowerInvariant()}")));
            var statusLabel = new RichTextLabel();
            statusLabel.SetMessage(status);
            entry.AddChild(statusLabel);
            var summary = new RichTextLabel();
            summary.SetMessage(Loc.GetString(activity.SummaryLocId));
            entry.AddChild(summary);
            if (KoronusActivityRuntimePolicy.IsPhysicalExecution(activity.Execution))
            {
                var challenge = new RichTextLabel();
                var key = KoronusActivityRuntimePolicy.IsSetPieceExecution(activity.Execution)
                    ? "koronus-sector-challenge-extract" :
                    KoronusActivityRuntimePolicy.IsHostileExecution(activity.Execution)
                        ? "koronus-sector-challenge-contain" : "koronus-sector-challenge-work";
                challenge.SetMessage(Loc.GetString(key,
                    ("seconds", (int) KoronusActivityChallengePolicy.GetWorkDuration(activity.Execution).TotalSeconds)) +
                    "\n" + Loc.GetString("koronus-sector-salvage-manifest",
                        ("manifest", Loc.GetString($"koronus-salvage-{KoronusActivityChallengePolicy.GetSalvageProfile(activity.Execution).ToString().ToLowerInvariant()}"))));
                entry.AddChild(challenge);
            }
            var locate = new Button { Text = Loc.GetString("koronus-sector-dossier-locate"), HorizontalExpand = true };
            locate.OnPressed += _ => SystemSelected?.Invoke(activity.SystemId);
            entry.AddChild(locate);
            _activities.AddChild(entry);
        }
        var journal = state.ActivityDossiers.Where(activity => activity.State == KoronusActivityState.Archived &&
            (selected == null || activity.SystemId == selected.Id)).Take(3).ToArray();
        if (journal.Length > 0)
        {
            AddText(Loc.GetString("koronus-sector-dossier-journal"));
            foreach (var entry in journal)
                AddText(Loc.GetString("koronus-sector-dossier-outcome", ("title", Loc.GetString(entry.TitleLocId)),
                    ("outcome", Loc.GetString($"koronus-sector-outcome-{entry.TerminalReason?.ToString().ToLowerInvariant()}"))));
        }
    }

    private void AddText(string text)
    {
        var label = new RichTextLabel();
        label.SetMessage(text);
        _activities.AddChild(label);
    }
}
