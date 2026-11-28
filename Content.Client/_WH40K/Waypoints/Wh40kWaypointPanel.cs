using System.Linq;
using System.Numerics;
using Content.Shared._WH40K.Waypoints;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.Waypoints;

/// <summary>
/// WH40K Heritage-styled overlay panel for managing ship-wide radar waypoints and issuing autopilot coordinates.
/// </summary>
public sealed class Wh40kWaypointPanel : PanelContainer
{
    private const float ListHeight = 180f;

    private static readonly Color BgColor = Color.FromHex("#100D0AF4");
    private static readonly Color BorderColor = Color.FromHex("#79603D");
    private static readonly Color RowBgColor = Color.FromHex("#18140FE0");
    private static readonly Color RowBorderColor = Color.FromHex("#56452D");
    private static readonly Color GoldColor = Color.FromHex("#E1C17E");
    private static readonly Color ParchmentColor = Color.FromHex("#E9E1D0");
    private static readonly Color MutedColor = Color.FromHex("#AD9F86");

    private readonly LineEdit _nameEdit;
    private readonly LineEdit _xEdit;
    private readonly LineEdit _yEdit;
    private readonly Button _pick;
    private readonly Button _add;
    private readonly Button _fly;
    private readonly RichTextLabel _status;
    private readonly Label _empty;
    private readonly BoxContainer _rows;

    private readonly List<Wh40kWaypoint> _shown = new();
    private NetEntity? _shownMap;
    private bool _canFly;

    public event Action<string, Vector2>? AddRequested;
    public event Action<int>? RemoveRequested;
    public event Action<Vector2>? FlyRequested;
    public event Action<bool>? PickToggled;

    public Wh40kWaypointPanel()
    {
        Name = "Wh40kWaypointPanel";
        SetWidth = 278;
        MouseFilter = MouseFilterMode.Stop;
        PanelOverride = new StyleBoxFlat(BgColor)
        {
            BorderColor = BorderColor,
            BorderThickness = new Thickness(2),
            ContentMarginLeftOverride = 7,
            ContentMarginRightOverride = 7,
            ContentMarginTopOverride = 6,
            ContentMarginBottomOverride = 7,
        };

        var header = new Label
        {
            Text = Loc.GetString("wh40k-waypoint-header"),
            FontColorOverride = GoldColor,
            StyleClasses = { "LabelHeading" },
            HorizontalExpand = true,
            ClipText = true,
        };

        var divider = new PanelContainer
        {
            MinHeight = 2,
            PanelOverride = new StyleBoxFlat(RowBorderColor),
        };

        _nameEdit = new LineEdit
        {
            Name = "Wh40kWaypointName",
            PlaceHolder = Loc.GetString("wh40k-waypoint-name"),
            HorizontalExpand = true,
            IsValid = text => text.Length <= Wh40kWaypointRules.MaxNameLength,
        };
        _xEdit = new LineEdit
        {
            Name = "Wh40kWaypointX",
            PlaceHolder = Loc.GetString("wh40k-waypoint-x"),
            HorizontalExpand = true,
        };
        _yEdit = new LineEdit
        {
            Name = "Wh40kWaypointY",
            PlaceHolder = Loc.GetString("wh40k-waypoint-y"),
            HorizontalExpand = true,
        };
        _pick = new Button
        {
            Name = "Wh40kWaypointPick",
            Text = Loc.GetString("wh40k-waypoint-pick"),
            ToolTip = Loc.GetString("wh40k-waypoint-pick-tooltip"),
            ToggleMode = true,
            HorizontalExpand = true,
            StyleClasses = { "ButtonSquare" },
        };
        _add = new Button
        {
            Name = "Wh40kWaypointAdd",
            Text = Loc.GetString("wh40k-waypoint-add"),
            ToolTip = Loc.GetString("wh40k-waypoint-add-tooltip"),
            HorizontalExpand = true,
            StyleClasses = { "ButtonSquare" },
        };
        _fly = new Button
        {
            Name = "Wh40kWaypointFly",
            Text = Loc.GetString("wh40k-waypoint-fly"),
            ToolTip = Loc.GetString("wh40k-waypoint-fly-tooltip"),
            Visible = false,
            HorizontalExpand = true,
            StyleClasses = { "ButtonSquare" },
        };
        _status = new RichTextLabel
        {
            Name = "Wh40kWaypointStatus",
            Visible = false,
        };
        _empty = new Label
        {
            Text = Loc.GetString("wh40k-waypoint-empty"),
            FontColorOverride = MutedColor,
        };
        _rows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3,
        };

        _pick.OnToggled += args => PickToggled?.Invoke(args.Pressed);
        _add.OnPressed += _ => SubmitAdd();
        _fly.OnPressed += _ => SubmitFly();

        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Children =
            {
                header,
                divider,
                _nameEdit,
                new BoxContainer { SeparationOverride = 4, Children = { _xEdit, _yEdit } },
                new BoxContainer { SeparationOverride = 4, Children = { _pick, _add, _fly } },
                _status,
                _empty,
                new ScrollContainer
                {
                    HScrollEnabled = false,
                    ReturnMeasure = true,
                    MaxHeight = ListHeight,
                    Children = { _rows },
                },
            },
        });
    }

    public void UpdateWaypoints(IReadOnlyList<Wh40kWaypoint> waypoints, NetEntity? map, bool canFly)
    {
        if (canFly == _canFly && map == _shownMap && _shown.SequenceEqual(waypoints))
            return;

        _shown.Clear();
        _shown.AddRange(waypoints);
        _shownMap = map;
        _canFly = canFly;
        _fly.Visible = canFly;
        _empty.Visible = _shown.Count == 0;
        _rows.RemoveAllChildren();
        foreach (var waypoint in _shown)
            _rows.AddChild(BuildRow(waypoint));
    }

    public void SetPicked(Vector2 position)
    {
        _xEdit.Text = $"{position.X:0.0}";
        _yEdit.Text = $"{position.Y:0.0}";
        _status.Visible = false;
        CancelPick();
    }

    public void CancelPick()
    {
        _pick.Pressed = false;
        PickToggled?.Invoke(false);
    }

    private PanelContainer BuildRow(Wh40kWaypoint waypoint)
    {
        var onMap = waypoint.Map == _shownMap;
        var fly = new Button
        {
            Name = $"Wh40kWaypointFly{waypoint.Id}",
            Text = Loc.GetString("wh40k-waypoint-go"),
            ToolTip = Loc.GetString(onMap ? "wh40k-waypoint-fly-tooltip" : "wh40k-waypoint-other-map"),
            Disabled = !onMap,
            Visible = _canFly,
            StyleClasses = { "ButtonSquare" },
        };
        fly.OnPressed += _ => Fly(waypoint.Position);

        var remove = new Button
        {
            Name = $"Wh40kWaypointRemove{waypoint.Id}",
            Text = Loc.GetString("wh40k-waypoint-remove"),
            ToolTip = Loc.GetString("wh40k-waypoint-remove-tooltip"),
            StyleClasses = { "ButtonSquare" },
        };
        remove.OnPressed += _ => RemoveRequested?.Invoke(waypoint.Id);

        return new PanelContainer
        {
            PanelOverride = new StyleBoxFlat(RowBgColor)
            {
                BorderColor = RowBorderColor,
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 5,
                ContentMarginRightOverride = 4,
                ContentMarginTopOverride = 3,
                ContentMarginBottomOverride = 3,
            },
            Children =
            {
                new BoxContainer
                {
                    SeparationOverride = 4,
                    Children =
                    {
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Vertical,
                            HorizontalExpand = true,
                            Children =
                            {
                                new Label
                                {
                                    Text = waypoint.Name,
                                    FontColorOverride = onMap ? ParchmentColor : MutedColor,
                                    ClipText = true,
                                },
                                new Label
                                {
                                    Text = onMap
                                        ? Loc.GetString("wh40k-waypoint-position", ("x", $"{waypoint.Position.X:0.0}"), ("y", $"{waypoint.Position.Y:0.0}"))
                                        : Loc.GetString("wh40k-waypoint-other-map"),
                                    ClipText = true,
                                    FontColorOverride = MutedColor,
                                },
                            },
                        },
                        fly,
                        remove,
                    },
                },
            },
        };
    }

    private bool TryReadPosition(out Vector2 position)
    {
        if (Wh40kWaypointRules.TryParsePosition(_xEdit.Text, _yEdit.Text, out position))
            return true;

        ShowStatus(Loc.GetString("wh40k-waypoint-status-invalid"));
        return false;
    }

    private void SubmitAdd()
    {
        if (_shown.Count >= Wh40kWaypointRules.MaxWaypoints)
        {
            ShowStatus(Loc.GetString("wh40k-waypoint-status-full", ("max", Wh40kWaypointRules.MaxWaypoints)));
            return;
        }

        if (!TryReadPosition(out var position))
            return;

        AddRequested?.Invoke(_nameEdit.Text, position);
        _nameEdit.Text = _xEdit.Text = _yEdit.Text = string.Empty;
        _status.Visible = false;
    }

    private void SubmitFly()
    {
        if (TryReadPosition(out var position))
            Fly(position);
    }

    private void Fly(Vector2 position)
    {
        FlyRequested?.Invoke(position);
        ShowStatus(Loc.GetString("wh40k-waypoint-status-flying", ("x", $"{position.X:0.0}"), ("y", $"{position.Y:0.0}")));
    }

    private void ShowStatus(string text)
    {
        _status.SetMessage(text, GoldColor);
        _status.Visible = true;
    }
}
