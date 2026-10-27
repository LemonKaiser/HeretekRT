using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Factions;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Factions;

public sealed partial class FactionRelationsPanel : BoxContainer
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly OptionButton _target = new() { HorizontalExpand = true, Name = "RelationTarget" };
    private readonly BoxContainer _content = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 12 };
    private readonly BoxContainer _editor = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
    private readonly BoxContainer _editorActions = new() { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8, Visible = false };
    private readonly Slider _score = new() { MinValue = -10, MaxValue = 10, Rounded = true, MinHeight = 28, HorizontalExpand = true, Name = "RelationScore" };
    private readonly Label _scoreLabel = FactionSocialUi.Heading("", 16);
    private readonly LineEdit _reason = new() { Name = "RelationReason" };
    private readonly FactionRelationGauge _previewGauge = new();
    private readonly LobbyFactionButton _save = FactionSocialUi.Button("wh40k-faction-rel-save", true);
    private readonly LobbyFactionButton _edit = FactionSocialUi.Button("wh40k-faction-rel-edit", true);
    private readonly RichTextLabel _help = FactionSocialUi.Body(Loc.GetString("wh40k-faction-rel-help"), true);
    private readonly RichTextLabel _limit = FactionSocialUi.Body("", true);
    private readonly RichTextLabel _preview = FactionSocialUi.Body("");
    private CompanyPrototype[] _factions = [];
    private FactionRelation[] _relations = [];
    private string? _source;
    private string? _selected;
    private int _revision;
    private bool _canEdit;
    private bool _pending;
    private bool _dirty;
    private bool _editing;
    private bool _admin;
    private int _baseScore;
    private TimeSpan _availableAt;
    private int CooldownSeconds => (int) Math.Max(0, Math.Ceiling((_availableAt - _timing.RealTime).TotalSeconds));
    private int _displayedCooldown = -1;
    public event Action<FactionSocialRequest>? SaveRequested;

    public FactionRelationsPanel()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 10;
        Margin = new Thickness(0, 12, 0, 0);
        HorizontalExpand = true;
        VerticalExpand = true;
        AddChild(_help);
        AddChild(_limit);
        var selector = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8 };
        selector.AddChild(_target);
        selector.AddChild(_edit);
        AddChild(selector);
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, ReserveScrollbarSpace = true };
        scroll.AddChild(_content);
        AddChild(scroll);
        _editorActions.AddChild(_save);
        var reload = FactionSocialUi.Button("wh40k-faction-rel-reload");
        reload.OnPressed += _ => { _dirty = false; _editing = false; Render(); };
        _editorActions.AddChild(reload);
        AddChild(_editorActions);
        _target.OnItemSelected += args =>
        {
            _target.SelectId(args.Id);
            _selected = _factions[args.Id].ID;
            _dirty = false;
            _editing = false;
            Render();
        };
        _edit.OnPressed += _ => BeginEdit();
        _scoreLabel.SetWidth = 48;
        _scoreLabel.Align = Label.AlignMode.Center;
        _score.BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#353330") };
        _score.FillStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.Transparent };
        _score.ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.Transparent };
        _score.GrabberStyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = FactionUiPresentation.Gold,
            ContentMarginLeftOverride = 5, ContentMarginRightOverride = 5,
            ContentMarginTopOverride = 10, ContentMarginBottomOverride = 10,
        };
        _score.OnValueChanged += _ => { _dirty = true; UpdateForm(); };
        _reason.OnTextChanged += _ => { _dirty = true; UpdateForm(); };
        _save.OnPressed += _ =>
        {
            if (_save.Disabled || _selected == null)
                return;
            SaveRequested?.Invoke(new FactionSocialRequest(Text: _reason.Text.Trim(), Target: _selected,
                Score: _admin ? (int) _score.Value : 0, Revision: _revision, Delta: _admin ? 0 : (int) _score.Value));
        };
    }

    public void UpdateRelations(string? source, FactionRelation[] relations, bool canEdit, bool pending = false,
        bool saved = false, int cooldownSeconds = 0, bool admin = false)
    {
        if (_admin != admin)
        {
            _score.SetValueWithoutEvent(0);
            _score.MinValue = admin ? -100 : -FactionSocialRules.MaxRelationAdjustment;
            _score.MaxValue = admin ? 100 : FactionSocialRules.MaxRelationAdjustment;
            _dirty = false;
            _editing = false;
        }
        _admin = admin;
        _availableAt = _timing.RealTime + TimeSpan.FromSeconds(Math.Max(0, cooldownSeconds));
        _displayedCooldown = -1;
        if (_source != source || saved || !canEdit)
        {
            _dirty = false;
            _editing = false;
        }
        _source = source;
        _relations = relations;
        _canEdit = canEdit;
        _pending = pending;
        _factions = _prototypes.EnumeratePrototypes<CompanyPrototype>().Where(f => !f.Disabled && f.ID != "None" && f.ID != source)
            .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        if (!_factions.Any(f => f.ID == _selected))
        {
            _selected = _factions.FirstOrDefault()?.ID;
            _dirty = false;
        }
        _target.Clear();
        for (var i = 0; i < _factions.Length; i++)
        {
            var other = _factions[i];
            var outgoing = Relation(source, other.ID);
            _target.AddItem($"{other.Name} · {Signed(outgoing.Score)} · {Loc.GetString(FactionSocialRules.StatusKey(outgoing.Score))}", i);
            if (other.ID == _selected)
                _target.SelectId(i);
        }
        _target.Disabled = pending;
        Render();
    }

    public void SetPending(bool pending)
    {
        _pending = pending;
        _target.Disabled = pending;
        UpdateForm();
    }

    public void BeginEdit()
    {
        if (!_canEdit || _pending || !_admin && CooldownSeconds > 0)
            return;
        _editing = true;
        Render();
    }

    private FactionRelation Relation(string? source, string target) =>
        _relations.FirstOrDefault(r => r.Source == source && r.Target == target || r.Source == target && r.Target == source) ?? new(source ?? "", target);

    private void Render()
    {
        // The editor survives background snapshots so an incoming transmission cannot erase a draft.
        _editor.Orphan();
        _content.RemoveAllChildren();
        _edit.Visible = _canEdit && !_editing;
        _editorActions.Visible = false;
        if (_source == null || _selected == null || !_prototypes.TryIndex<CompanyPrototype>(_source, out var source) ||
            !_prototypes.TryIndex<CompanyPrototype>(_selected, out var target))
        {
            _content.AddChild(FactionSocialUi.Body(Loc.GetString("wh40k-faction-rel-empty"), true));
            return;
        }
        var outgoing = Relation(source.ID, target.ID);
        _baseScore = outgoing.Score;
        var diagram = FactionSocialUi.Card();
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 16, MinHeight = 156 };
        row.AddChild(Emblem(source));
        var lanes = new BoxContainer { Orientation = LayoutOrientation.Vertical, VerticalAlignment = VAlignment.Center, HorizontalExpand = true, SeparationOverride = 6 };
        var commonHeading = FactionSocialUi.Heading(Loc.GetString("wh40k-faction-rel-common"), 14);
        commonHeading.Align = Label.AlignMode.Center;
        lanes.AddChild(commonHeading);
        lanes.AddChild(ScoreLabel(outgoing.Score));
        lanes.AddChild(new FactionRelationArrow(outgoing.Score));
        row.AddChild(lanes);
        row.AddChild(Emblem(target));
        diagram.AddChild(row);
        diagram.Visible = !_editing;
        _content.AddChild(diagram);
        if (!_canEdit || !_editing)
        {
            AddPosition(Loc.GetString("wh40k-faction-rel-common"), outgoing);
            UpdateForm();
            return;
        }
        _editorActions.Visible = true;
        if (!_dirty)
        {
            _revision = outgoing.Revision;
            _score.SetValueWithoutEvent(_admin ? outgoing.Score : 0);
            _reason.Text = outgoing.Reason;
        }
        _editor.RemoveAllChildren();
        _score.Orphan();
        _scoreLabel.Orphan();
        _editor.AddChild(FactionSocialUi.Heading(Loc.GetString("wh40k-faction-rel-editor"), 16));
        _editor.AddChild(FactionSocialUi.Body($"{source.Name} ↔ {target.Name}", true));
        _editor.AddChild(FactionSocialUi.Body(Loc.GetString(_admin ? "wh40k-faction-rel-admin-adjust-help" : "wh40k-faction-rel-adjust-help"), true));
        var scoreRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 14 };
        scoreRow.AddChild(_score);
        scoreRow.AddChild(_scoreLabel);
        _editor.AddChild(scoreRow);
        _preview.Orphan();
        _editor.AddChild(_preview);
        _editor.AddChild(_previewGauge);
        var limits = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        limits.AddChild(new Label { Text = Loc.GetString("wh40k-faction-rel-scale-enemy"), FontColorOverride = RelationColor(-100) });
        limits.AddChild(new Label { Text = "0", HorizontalExpand = true, Align = Label.AlignMode.Center, FontColorOverride = FactionUiPresentation.Muted });
        limits.AddChild(new Label { Text = Loc.GetString("wh40k-faction-rel-scale-ally"), FontColorOverride = RelationColor(100) });
        _editor.AddChild(limits);
        _reason.PlaceHolder = Loc.GetString("wh40k-faction-rel-reason");
        _editor.AddChild(_reason);
        _content.AddChild(_editor);
        AddPosition(Loc.GetString("wh40k-faction-rel-common"), outgoing);
        UpdateForm();
    }

    private void AddPosition(string name, FactionRelation relation)
    {
        var card = FactionSocialUi.Card();
        var column = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 6 };
        column.AddChild(FactionSocialUi.Heading(name, 14));
        column.AddChild(new FactionRelationGauge { Score = relation.Score });
        column.AddChild(FactionSocialUi.Body(string.IsNullOrWhiteSpace(relation.Reason) ? Loc.GetString("wh40k-faction-rel-no-reason") : relation.Reason, true));
        if (relation.Revision > 0)
            column.AddChild(FactionSocialUi.Body(Loc.GetString("wh40k-faction-rel-updated", ("author", relation.Author),
                ("date", relation.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))), true));
        card.AddChild(column);
        _content.AddChild(card);
    }

    private static Control Emblem(CompanyPrototype faction)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SetWidth = 124, SeparationOverride = 6, VerticalAlignment = VAlignment.Center };
        var image = new TextureRect { SetSize = new Vector2(112), CanShrink = true, Stretch = TextureRect.StretchMode.KeepAspectCentered, HorizontalAlignment = HAlignment.Center };
        if (faction.Image is { } path)
            image.TexturePath = path;
        box.AddChild(image);
        box.AddChild(new Label { Text = faction.Name, ClipText = true, ToolTip = faction.Name, Align = Label.AlignMode.Center, FontColorOverride = FactionUiPresentation.Text });
        return box;
    }

    private static Label ScoreLabel(int score) => new()
    {
        Text = $"{Signed(score)} · {Loc.GetString(FactionSocialRules.StatusKey(score))}",
        Align = Label.AlignMode.Center, FontColorOverride = RelationColor(score),
    };

    private void UpdateForm()
    {
        var value = (int) _score.Value;
        _scoreLabel.Text = Signed(value);
        _scoreLabel.FontColorOverride = RelationColor(value);
        var result = _admin ? value : FactionSocialRules.AdjustRelation(_baseScore, value);
        _previewGauge.Score = result;
        _preview.SetMessage(Loc.GetString("wh40k-faction-rel-adjust-preview", ("current", Signed(_baseScore)), ("result", Signed(result))), RelationColor(result));
        var seconds = CooldownSeconds;
        var waiting = !_admin && seconds > 0;
        _score.Disabled = _pending || waiting;
        _edit.Disabled = _pending || waiting;
        _reason.Editable = !_pending && !waiting;
        _limit.Visible = _canEdit && !_admin;
        if (_displayedCooldown != seconds)
        {
            _displayedCooldown = seconds;
            _limit.SetMessage(waiting ? Loc.GetString("wh40k-faction-rel-cooldown", ("time", TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)))
                : Loc.GetString("wh40k-faction-rel-adjust-available"), FactionUiPresentation.Gold);
        }
        _save.Disabled = _pending || waiting || !_canEdit || !_dirty ||
            !_admin && (!FactionSocialRules.ValidRelationAdjustment(value) || result == _baseScore) ||
            !FactionSocialRules.ValidText(_reason.Text, FactionSocialRules.MaxReasonLength);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (CooldownSeconds != _displayedCooldown)
            UpdateForm();
    }

    internal static string Signed(int score) => score.ToString("+0;−0;0", CultureInfo.InvariantCulture);
    internal static Color RelationColor(int score) => Color.FromHex(score <= -60 ? "#DC7770" : score < -20 ? "#CD9A72" : score <= 20 ? "#ACA99F" : score < 60 ? "#92B9A5" : "#8DCAA0");
}

internal sealed class FactionRelationArrow(int score) : Control
{
    protected override Vector2 MeasureOverride(Vector2 availableSize) => new(0, 12);
    protected override void Draw(DrawingHandleScreen handle)
    {
        var start = new Vector2(4, PixelHeight / 2);
        var end = new Vector2(PixelWidth - 4, PixelHeight / 2);
        var color = FactionRelationsPanel.RelationColor(score);
        handle.DrawLine(start, end, color);
        handle.DrawLine(end, end + new Vector2(-7, -4) * UIScale, color);
        handle.DrawLine(end, end + new Vector2(-7, 4) * UIScale, color);
        handle.DrawLine(start, start + new Vector2(7, -4) * UIScale, color);
        handle.DrawLine(start, start + new Vector2(7, 4) * UIScale, color);
    }
}

internal sealed class FactionRelationGauge : Control
{
    public int Score;
    protected override Vector2 MeasureOverride(Vector2 availableSize) => new(0, 14);
    protected override void Draw(DrawingHandleScreen handle)
    {
        var width = PixelWidth;
        var y = PixelHeight / 2f;
        for (var i = 0; i < 40; i++)
        {
            var left = i * width / 40;
            handle.DrawRect(new UIBox2(left, y - 2 * UIScale, (i + 1) * width / 40 - UIScale, y + 2 * UIScale),
                FactionRelationsPanel.RelationColor(i * 5 - 100).WithAlpha(0.55f));
        }
        var marker = Math.Clamp((Score + 100) / 200f * (width - 6 * UIScale) + 3 * UIScale, 0, width);
        handle.DrawLine(new Vector2(width / 2, 0), new Vector2(width / 2, PixelHeight), Color.White.WithAlpha(0.25f));
        handle.DrawCircle(new Vector2(marker, y), 4 * UIScale, FactionRelationsPanel.RelationColor(Score));
    }
}
