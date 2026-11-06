using System.Linq;
using System.Numerics;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Administration.Events;
using Content.Shared.Roles;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Administration.Events;

public sealed partial class EventAdminWindow : FancyWindow
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    private EventAdminEuiState _state = new();
    private readonly HashSet<Guid> _selection = new();
    private readonly List<EventAdminPlayer> _visible = new();
    private readonly Dictionary<Guid, CheckBox> _checks = new();
    private readonly List<(Button Button, Func<bool> Enabled, EventAdminAction? Action)> _buttons = new();
    private readonly List<ConfirmButton> _confirmations = new();
    private readonly BoxContainer _rows = Vertical();
    private readonly LineEdit _search = new();
    private readonly OptionButton _teamFilter = new();
    private readonly OptionButton _statusFilter = new();
    private readonly OptionButton _team = new();
    private readonly OptionButton _point = new();
    private readonly Label _summary = new() { ClipText = true };
    private readonly Label _selectionLabel = new() { ClipText = true };
    private readonly Label _teamInfo = new() { ClipText = true };
    private readonly Label _friendlyFireInfo = new() { ClipText = true };
    private readonly Label _timer = new();
    private readonly RichTextLabel _result = new();
    private readonly LineEdit _teamName = new();
    private readonly LineEdit _pointName = new();
    private readonly LineEdit _score = new() { Text = "0" };
    private readonly LineEdit _seconds = new() { Text = "300" };
    private readonly LineEdit _item = new();
    private readonly LineEdit _amount = new() { Text = "1" };
    private readonly LineEdit _outfit = new();
    private readonly TextEdit _message = new() { SetHeight = 110 };
    private readonly ItemList _catalog = new() { SetHeight = 180 };
    private readonly LineEdit _catalogSearch = new();
    private readonly OptionButton _catalogType = new();
    private readonly Label _catalogInfo = new() { ClipText = true };
    private readonly List<string> _catalogIds = new();
    private readonly TabContainer _tabs = new() { VerticalExpand = true, HorizontalExpand = true };
    private bool _applying;
    private bool _pending;
    private int _nextRequest;
    private int _pendingRequest;
    private EventAdminAction? _requestedAction;
    private string _requestedText = "";
    private string _teamNames = "";
    private string _pointNames = "";

    public event Action<EventAdminRequest>? ActionRequested;

    public EventAdminWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = T("title");
        MinSize = new Vector2(1020, 620);
        SetSize = new Vector2(1180, 760);
        var root = Vertical();
        root.Margin = new Thickness(12);
        root.SeparationOverride = 10;
        ContentsContainer.AddChild(root);
        var heading = new Label { Text = T("heading"), StyleClasses = { "LabelHeadingBigger" } };
        root.AddChild(heading);
        root.AddChild(new Label { Text = T("subtitle"), Modulate = Color.FromHex("#A9B6C7"), ClipText = true });
        root.AddChild(_summary);
        var body = new BoxContainer { HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 14 };
        root.AddChild(body);
        var roster = Vertical();
        roster.SetWidth = 420;
        roster.HorizontalExpand = false;
        body.AddChild(roster);
        roster.AddChild(Heading("players"));
        _search.PlaceHolder = T("search");
        roster.AddChild(_search);
        var filters = new BoxContainer { SeparationOverride = 6 };
        _teamFilter.HorizontalExpand = true;
        _statusFilter.HorizontalExpand = true;
        filters.AddChild(_teamFilter); filters.AddChild(_statusFilter);
        roster.AddChild(filters);
        foreach (var (key, index) in new[] { ("filter-all", 0), ("filter-body", 1), ("filter-dead", 2),
                     ("filter-frozen", 3), ("filter-god", 4), ("filter-offline", 5) })
            _statusFilter.AddItem(T(key), index);
        var select = new BoxContainer { SeparationOverride = 4 };
        select.AddChild(LocalButton("select-visible", () => SelectVisible(0)));
        select.AddChild(LocalButton("invert", () => SelectVisible(1)));
        select.AddChild(LocalButton("clear", () => { _selection.Clear(); ResetConfirmations(); SyncChecks(); }));
        roster.AddChild(select);
        roster.AddChild(_selectionLabel);
        roster.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _rows } });
        var refresh = LocalButton("refresh", () => Request(EventAdminAction.Refresh));
        roster.AddChild(refresh);
        _buttons.Add((refresh, () => true, EventAdminAction.Refresh));
        body.AddChild(_tabs);

        BuildActions();
        BuildTeams();
        BuildSupply();
        BuildBroadcast();
        foreach (var (key, index) in new[] { ("tab-actions", 0), ("tab-teams", 1), ("tab-supply", 2), ("tab-broadcast", 3) })
            _tabs.SetTabTitle(index, T(key));
        var footer = Card(root);
        footer.AddChild(_result);
        _search.OnTextChanged += _ => RenderRoster();
        _teamFilter.OnItemSelected += args => { _teamFilter.SelectId(args.Id); RenderRoster(); };
        _statusFilter.OnItemSelected += args => { _statusFilter.SelectId(args.Id); RenderRoster(); };
        _team.OnItemSelected += args =>
        {
            _team.SelectId(args.Id);
            _score.Text = (_state.Teams.FirstOrDefault(t => t.Id == args.Id)?.Score ?? 0).ToString();
            ResetConfirmations(); UpdateTeamInfo(); UpdateButtons();
        };
        _point.OnItemSelected += args => { _point.SelectId(args.Id); ResetConfirmations(); UpdateButtons(); };
        foreach (var field in new[] { _teamName, _pointName, _score, _seconds, _item, _amount, _outfit })
            field.OnTextChanged += _ => { ResetConfirmations(); UpdateButtons(); };
        _catalogSearch.OnTextChanged += _ => RenderCatalog();
        _message.OnTextChanged += _ => { ResetConfirmations(); UpdateButtons(); };
        _catalogType.OnItemSelected += args => { _catalogType.SelectId(args.Id); RenderCatalog(); };
        _catalog.OnItemSelected += args =>
        {
            if (args.ItemIndex >= _catalogIds.Count) return;
            if (_catalogType.SelectedId == 0) _item.Text = _catalogIds[args.ItemIndex];
            else _outfit.Text = _catalogIds[args.ItemIndex];
        };
        RenderCatalog();
        Apply(new EventAdminEuiState());
    }

    private void BuildActions()
    {
        var page = Page();
        var movement = Card(page, "movement");
        var grid = Grid(movement);
        Add(grid, EventAdminAction.Bring, () => Request(EventAdminAction.Bring), HasBody);
        Add(grid, EventAdminAction.GoTo, () => Request(EventAdminAction.GoTo), () => _selection.Count == 1 && HasBody());
        Add(grid, EventAdminAction.Return, () => Request(EventAdminAction.Return), () => Chosen().Any(p => p.CanReturn));
        movement.AddChild(Hint("return-hint"));
        _point.HorizontalExpand = true;
        movement.AddChild(_point);
        grid = Grid(movement);
        Add(grid, EventAdminAction.TeleportToPoint, () => Request(EventAdminAction.TeleportToPoint), () => HasBody() && ValidPoint());
        Add(grid, EventAdminAction.GoToPoint, () => Request(EventAdminAction.GoToPoint), ValidPoint);
        var pointEdit = new BoxContainer { SeparationOverride = 6 };
        _pointName.PlaceHolder = T("point-name"); _pointName.HorizontalExpand = true;
        pointEdit.AddChild(_pointName);
        Add(pointEdit, EventAdminAction.CreatePoint, () => Request(EventAdminAction.CreatePoint, text: _pointName.Text),
            () => _pointName.Text.Trim().Length is > 0 and <= EventAdminLimits.NameLength && _state.Points.Length < EventAdminLimits.Points);
        movement.AddChild(pointEdit);
        Add(movement, EventAdminAction.DeletePoint, () => Request(EventAdminAction.DeletePoint), () => _point.SelectedId >= 0, confirm: true);

        var control = Card(page, "control");
        grid = Grid(control);
        foreach (var action in new[] { EventAdminAction.Freeze, EventAdminAction.Unfreeze, EventAdminAction.FreezeMute,
                     EventAdminAction.Heal, EventAdminAction.GodOn, EventAdminAction.GodOff, EventAdminAction.Sleep,
                     EventAdminAction.Wake, EventAdminAction.Extinguish, EventAdminAction.DropHands, EventAdminAction.RestoreEffects })
        {
            var captured = action;
            Add(grid, action, () => Request(captured), HasBody);
        }
        control.AddChild(Hint("restore-hint"));
    }

    private void BuildTeams()
    {
        var page = Page();
        var setup = Card(page, "teams");
        setup.AddChild(Hint("team-hint"));
        _teamName.PlaceHolder = T("team-name"); setup.AddChild(_teamName);
        Add(setup, EventAdminAction.CreateTeam, () => Request(EventAdminAction.CreateTeam, text: _teamName.Text),
            () => _teamName.Text.Trim().Length is > 0 and <= EventAdminLimits.NameLength && _state.Teams.Length < EventAdminLimits.Teams);
        setup.AddChild(_team);
        setup.AddChild(_teamInfo);
        var grid = Grid(setup);
        Add(grid, EventAdminAction.RenameTeam, () => Request(EventAdminAction.RenameTeam, text: _teamName.Text),
            () => ValidTeam() && _teamName.Text.Trim().Length is > 0 and <= EventAdminLimits.NameLength);
        Add(grid, EventAdminAction.DeleteTeam, () => Request(EventAdminAction.DeleteTeam), ValidTeam, confirm: true);
        var friendlyFire = Card(page, "friendly-fire");
        friendlyFire.AddChild(_friendlyFireInfo);
        friendlyFire.AddChild(Hint("friendly-fire-hint"));
        grid = Grid(friendlyFire);
        Add(grid, EventAdminAction.FriendlyFireOff, () => Request(EventAdminAction.FriendlyFireOff),
            () => _state.Teams.Any(t => t.Id == _team.SelectedId && t.FriendlyFire));
        Add(grid, EventAdminAction.FriendlyFireOn, () => Request(EventAdminAction.FriendlyFireOn),
            () => _state.Teams.Any(t => t.Id == _team.SelectedId && !t.FriendlyFire));
        var membership = Card(page, "membership");
        var selectTeam = LocalButton("select-team", () =>
        {
            _selection.Clear();
            foreach (var player in _state.Players.Where(p => p.Team == _team.SelectedId)) _selection.Add(player.Id);
            ResetConfirmations();
            SyncChecks();
        });
        membership.AddChild(selectTeam);
        _buttons.Add((selectTeam, ValidTeam, null));
        grid = Grid(membership);
        Add(grid, EventAdminAction.AssignTeam, () => Request(EventAdminAction.AssignTeam), () => HasSelection() && ValidTeam());
        Add(grid, EventAdminAction.UnassignTeam, () => Request(EventAdminAction.UnassignTeam), HasSelection);
        Add(membership, EventAdminAction.BalanceTeams, () => Request(EventAdminAction.BalanceTeams), () => HasSelection() && _state.Teams.Length >= 2);
        membership.AddChild(Hint("balance-hint"));
        var scoring = Card(page, "scoring");
        scoring.AddChild(_score);
        Add(scoring, EventAdminAction.SetScore, () => Request(EventAdminAction.SetScore, number: Number(_score)),
            () => ValidTeam() && ValidNumber(_score, -99999, 99999));
        var reset = Card(page, "organisation");
        Add(reset, EventAdminAction.Reset, () => Request(EventAdminAction.Reset), confirm: true);
        reset.AddChild(Hint("reset-hint"));
    }

    private void BuildSupply()
    {
        var page = Page();
        var catalog = Card(page, "catalog");
        _catalogSearch.PlaceHolder = T("catalog-search"); catalog.AddChild(_catalogSearch);
        _catalogType.AddItem(T("items"), 0); _catalogType.AddItem(T("outfits"), 1);
        catalog.AddChild(_catalogType); catalog.AddChild(_catalog); catalog.AddChild(_catalogInfo);
        var items = Card(page, "give-item");
        _item.PlaceHolder = T("item-id"); items.AddChild(_item);
        items.AddChild(Hint("amount")); items.AddChild(_amount);
        Add(items, EventAdminAction.GiveItem, () => Request(EventAdminAction.GiveItem, _item.Text, Number(_amount)),
            () => HasBody() && ValidNumber(_amount, 1, 10) && _prototypes.TryIndex<EntityPrototype>(_item.Text.Trim(), out var proto) &&
                  !proto.Abstract && proto.Components.ContainsKey("Item"));
        var outfits = Card(page, "equip-outfit");
        _outfit.PlaceHolder = T("outfit-id"); outfits.AddChild(_outfit);
        Add(outfits, EventAdminAction.EquipOutfit, () => Request(EventAdminAction.EquipOutfit, text: _outfit.Text),
            () => HasBody() && _prototypes.HasIndex<StartingGearPrototype>(_outfit.Text.Trim()), confirm: true);
        outfits.AddChild(Hint("outfit-hint"));
    }

    private void BuildBroadcast()
    {
        var page = Page();
        var messaging = Card(page, "messaging");
        _message.Placeholder = new Rope.Leaf(T("message-placeholder"));
        messaging.AddChild(_message);
        Add(messaging, EventAdminAction.MessageSelection, () => Request(EventAdminAction.MessageSelection, text: Rope.Collapse(_message.TextRope)),
            () => HasSelection() && _message.TextLength is > 0 and <= EventAdminLimits.MessageLength);
        Add(messaging, EventAdminAction.Announce, () => Request(EventAdminAction.Announce, text: Rope.Collapse(_message.TextRope)),
            () => _message.TextLength is > 0 and <= EventAdminLimits.MessageLength, confirm: true);
        messaging.AddChild(Hint("message-hint"));
        var timer = Card(page, "timer");
        _timer.AddStyleClass("LabelHeadingBigger"); timer.AddChild(_timer);
        timer.AddChild(Hint("seconds")); timer.AddChild(_seconds);
        var grid = Grid(timer);
        Add(grid, EventAdminAction.StartTimer, () => Request(EventAdminAction.StartTimer, number: Number(_seconds)), () => ValidNumber(_seconds, 1, EventAdminLimits.TimerSeconds));
        Add(grid, EventAdminAction.PauseTimer, () => Request(EventAdminAction.PauseTimer), () => _state.TimerRunning);
        Add(grid, EventAdminAction.ResumeTimer, () => Request(EventAdminAction.ResumeTimer), () => !_state.TimerRunning && _state.SecondsLeft > 0);
        Add(grid, EventAdminAction.StopTimer, () => Request(EventAdminAction.StopTimer), () => _state.TimerRunning || _state.SecondsLeft > 0);
        timer.AddChild(Hint("timer-hint"));
    }

    public void Apply(EventAdminEuiState state)
    {
        if (_state.Epoch != state.Epoch) { _selection.Clear(); ResetConfirmations(); }
        _state = state;
        _pending = _pending && state.CompletedRequest < _pendingRequest;
        _selection.RemoveWhere(id => !state.Players.Any(p => p.Id == id));
        var teams = string.Join("|", state.Teams.Select(t => $"{t.Id}:{t.Name}"));
        if (teams != _teamNames || _teamFilter.ItemCount == 0)
        {
            ResetConfirmations();
            _teamNames = teams;
            Refill(_team, state.Teams.Select(t => (t.Id, t.Name)), "no-team");
            Refill(_teamFilter, state.Teams.Select(t => (t.Id, t.Name)), "filter-all-teams", unassigned: true);
            RenderRoster(true);
        }
        var points = string.Join("|", state.Points.Select(p => $"{p.Id}:{p.Name}:{p.Valid}"));
        if (points != _pointNames || _point.ItemCount == 0)
        {
            _pointNames = points;
            Refill(_point, state.Points.Select(p => (p.Id, p.Valid ? p.Name : $"{p.Name} ({T("point-unavailable")})")), "no-point");
        }
        if (!_pending && !state.Busy && _requestedAction != null)
        {
            if (state.Result.Applied > 0)
            {
                if (_requestedAction == EventAdminAction.CreateTeam && state.Teams.FirstOrDefault(t => t.Name == _requestedText.Trim()) is { } createdTeam)
                    _team.SelectId(createdTeam.Id);
                if (_requestedAction == EventAdminAction.CreatePoint && state.Points.FirstOrDefault(p => p.Name == _requestedText.Trim()) is { } createdPoint)
                    _point.SelectId(createdPoint.Id);
            }
            _requestedAction = null;
        }
        _summary.Text = T("summary", ("online", state.Players.Count(p => p.Online)), ("teams", state.Teams.Length), ("points", state.Points.Length));
        _timer.Text = $"{state.SecondsLeft / 60:00}:{state.SecondsLeft % 60:00}  ·  {T(state.TimerRunning ? "running" : "paused")}";
        _result.SetMessage(state.Busy || _pending ? T("busy") : string.IsNullOrEmpty(state.Result.Key) ? T("ready") :
            Loc.GetString(state.Result.Key, ("applied", state.Result.Applied), ("skipped", state.Result.Skipped)));
        RenderRoster();
        UpdateTeamInfo();
        UpdateButtons();
    }

    private void RenderRoster(bool force = false)
    {
        var search = _search.Text.Trim();
        var players = _state.Players.Where(p =>
            (p.Account.Contains(search, StringComparison.OrdinalIgnoreCase) || p.Character.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (_teamFilter.SelectedId == -1 || (_teamFilter.SelectedId == -2 ? p.Team < 0 : p.Team == _teamFilter.SelectedId)) &&
            (_statusFilter.SelectedId switch { 1 => p.HasBody && !p.Dead && !p.Ghost, 2 => p.Dead, 3 => p.Frozen, 4 => p.God, 5 => !p.Online, _ => true })).ToList();
        if (!force && _visible.SequenceEqual(players)) { SyncChecks(); return; }
        _visible.Clear(); _visible.AddRange(players);
        _checks.Clear(); _rows.RemoveAllChildren();
        foreach (var player in players)
        {
            var row = Card(_rows);
            row.SeparationOverride = 2;
            var check = new CheckBox { Text = string.IsNullOrEmpty(player.Character) ? player.Account : player.Character, Pressed = _selection.Contains(player.Id) };
            check.Label.ClipText = true; check.Label.HorizontalExpand = true;
            check.HorizontalExpand = true;
            check.ToolTip = $"{player.Character} / {player.Account}\n{player.Id}";
            var header = new BoxContainer { SeparationOverride = 6 };
            header.AddChild(check);
            row.AddChild(header);
            var team = _state.Teams.FirstOrDefault(t => t.Id == player.Team)?.Name ?? T("unassigned");
            var label = new Label { Text = $"@{player.Account}  ·  {team}", ClipText = true, Modulate = Color.FromHex("#B1BED0") };
            row.AddChild(label);
            var flags = new List<string> { T(!player.Online ? "offline" : player.Ghost ? "observer" : !player.HasBody ? "no-body-short" : player.Dead ? "dead" : player.Sleeping ? "sleeping" : "alive") };
            if (player.Frozen) flags.Add(T("frozen-short"));
            if (player.God) flags.Add("GOD");
            if (player.CanReturn) flags.Add("R");
            header.AddChild(new Label { Text = string.Join(" · ", flags), ToolTip = T("status-hint"),
                Modulate = Color.FromHex(player.Dead ? "#EB9191" : player.Online ? "#8ACCB0" : "#9AA1AE") });
            _checks.Add(player.Id, check);
            check.OnToggled += args =>
            {
                if (_applying) return;
                if (args.Pressed) _selection.Add(player.Id); else _selection.Remove(player.Id);
                ResetConfirmations(); UpdateSelection(); UpdateButtons();
            };
        }
        UpdateSelection();
    }

    private void SelectVisible(int mode)
    {
        ResetConfirmations();
        foreach (var player in _visible)
            if (mode == 1 && _selection.Contains(player.Id)) _selection.Remove(player.Id); else _selection.Add(player.Id);
        SyncChecks();
    }

    private void SyncChecks()
    {
        _applying = true;
        foreach (var (id, check) in _checks) check.Pressed = _selection.Contains(id);
        _applying = false;
        UpdateSelection(); UpdateButtons();
    }

    private void UpdateSelection() => _selectionLabel.Text = T("selected", ("count", _selection.Count),
        ("visible", _visible.Count(p => _selection.Contains(p.Id))));
    private IEnumerable<EventAdminPlayer> Chosen() => _state.Players.Where(p => _selection.Contains(p.Id));
    private bool HasSelection() => _selection.Count > 0;
    private bool HasBody() => Chosen().Any(p => p.HasBody);
    private bool ValidTeam() => _state.Teams.Any(t => t.Id == _team.SelectedId);
    private bool ValidPoint() => _state.Points.Any(p => p.Id == _point.SelectedId && p.Valid);
    private void UpdateTeamInfo()
    {
        var team = _state.Teams.FirstOrDefault(t => t.Id == _team.SelectedId);
        _teamInfo.Text = team == null ? T("choose-team") : T("team-info", ("members", team.Members), ("score", team.Score));
        _friendlyFireInfo.Text = team == null ? T("choose-team") : T(team.FriendlyFire ? "friendly-fire-on" : "friendly-fire-off");
    }

    private void Request(EventAdminAction action, string text = "", int number = 0)
    {
        if (_pending || _state.Busy) return;
        _pendingRequest = ++_nextRequest;
        _requestedAction = action;
        _requestedText = text;
        _pending = true; ResetConfirmations(); UpdateButtons();
        _result.SetMessage(T("busy"));
        ActionRequested?.Invoke(new EventAdminRequest(action, _state.Epoch,
            EventAdminAccess.UsesSelection(action) ? _selection.ToArray() : [], _team.SelectedId,
            _point.SelectedId, text, number, _pendingRequest));
    }

    private void UpdateButtons()
    {
        foreach (var (button, enabled, action) in _buttons)
        {
            var allowed = action == null || _state.AllowedActions.Contains(action.Value);
            var tooMany = action is { } selectedAction && EventAdminAccess.UsesSelection(selectedAction) && _selection.Count > EventAdminLimits.Targets;
            button.Disabled = _pending || _state.Busy || _state.Epoch == 0 || !allowed || tooMany || !enabled();
            button.ToolTip = !allowed ? T("denied") : tooMany ? T("selection-limit", ("max", EventAdminLimits.Targets)) : null;
        }
    }

    private void ResetConfirmations()
    {
        foreach (var button in _confirmations) { button.IsConfirming = false; button.Text = button.Text; }
    }

    private void RenderCatalog()
    {
        _catalog.Clear(); _catalogIds.Clear();
        var search = _catalogSearch.Text.Trim();
        var entries = _catalogType.SelectedId == 0
            ? _prototypes.EnumeratePrototypes<EntityPrototype>().Where(p => !p.Abstract && p.Components.ContainsKey("Item"))
                .Select(p => (p.ID, Name: p.Name))
            : _prototypes.EnumeratePrototypes<StartingGearPrototype>().Select(p => (p.ID, Name: p.ID));
        foreach (var (id, name) in entries.Where(p => p.ID.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                     p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.ID).Take(200))
        {
            _catalogIds.Add(id); _catalog.AddItem($"{name} · {id}");
        }
        _catalogInfo.Text = T("catalog-count", ("count", _catalogIds.Count));
    }

    private BoxContainer Page()
    {
        var page = Vertical(); page.Margin = new Thickness(0, 8, 0, 0); page.SeparationOverride = 10;
        _tabs.AddChild(new ScrollContainer { HScrollEnabled = false, Children = { page } });
        return page;
    }
    private static BoxContainer Vertical() => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 6 };
    private static Label Heading(string key) => new() { Text = T(key), StyleClasses = { "LabelHeading" } };
    private static RichTextLabel Hint(string key)
    {
        var hint = new RichTextLabel { HorizontalExpand = true, Modulate = Color.FromHex("#A9B6C7") };
        hint.SetMessage(T(key));
        return hint;
    }
    private static BoxContainer Card(Control parent, string? heading = null)
    {
        var content = Vertical(); content.Margin = new Thickness(8);
        var panel = new PanelContainer { PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#202833") } };
        panel.AddChild(content); parent.AddChild(panel);
        if (heading != null) content.AddChild(Heading(heading));
        return content;
    }
    private static GridContainer Grid(Control parent)
    {
        var grid = new GridContainer { Columns = 2, HorizontalExpand = true };
        parent.AddChild(grid); return grid;
    }
    private static Button LocalButton(string key, Action pressed)
    {
        var button = new Button { Text = T(key), HorizontalExpand = true, MinHeight = 28 };
        button.OnPressed += _ => pressed(); return button;
    }
    private void Add(Control parent, EventAdminAction action, Action pressed, Func<bool>? enabled = null, bool confirm = false)
    {
        Button button;
        if (confirm)
        {
            var confirmation = new ConfirmButton { Text = T("action-" + action.ToString().ToLowerInvariant()),
                ConfirmationText = T("confirm"), ResetTime = TimeSpan.FromSeconds(5) };
            confirmation.OnPressed += _ => pressed(); _confirmations.Add(confirmation); button = confirmation;
            button.AddStyleClass(StyleNano.ButtonCaution);
        }
        else button = LocalButton("action-" + action.ToString().ToLowerInvariant(), pressed);
        button.HorizontalExpand = true; button.MinHeight = 30;
        parent.AddChild(button); _buttons.Add((button, enabled ?? (() => true), action));
    }
    private static void Refill(OptionButton option, IEnumerable<(int Id, string Name)> choices, string empty, bool unassigned = false)
    {
        var old = option.ItemCount == 0 ? -1 : option.SelectedId;
        option.Clear(); option.AddItem(T(empty), -1);
        if (unassigned) option.AddItem(T("unassigned"), -2);
        foreach (var (id, name) in choices) option.AddItem(name, id);
        option.SelectId(Enumerable.Range(0, option.ItemCount).Any(i => option.GetItemId(i) == old) ? old : -1);
    }
    private static int Number(LineEdit input) => int.TryParse(input.Text, out var number) ? number : 0;
    private static bool ValidNumber(LineEdit input, int min, int max) => int.TryParse(input.Text, out var value) && value >= min && value <= max;
    private static string T(string key, params (string, object)[] args) => Loc.GetString("wh40k-event-" + key, args);
}
