using System.Numerics;
using Content.Client.Message;
using Content.Client.UserInterface.Controls;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Timing;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.UserInterface.Systems.Atmos.GasTank;

public sealed partial class GasTankWindow
    : FancyWindow
{
    [Dependency] private IEntityManager _entManager = default!;

    private readonly RichTextLabel _lblPressure;
    private readonly FloatSpinBox _spbPressure;
    private readonly RichTextLabel _lblInternals;
    private readonly Button _btnInternals;

    public EntityUid Entity;

    public event Action<float>? OnOutputPressure;
    public event Action? OnToggleInternals;

    public GasTankWindow()
    {
        IoCManager.InjectDependencies(this);
        MinSize = new Vector2(380, 240);
        SetSize = new Vector2(420, 250);
        var contentContainer = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 4,
        };
        ContentsContainer.AddChild(contentContainer);

        _lblPressure = new RichTextLabel();
        contentContainer.AddChild(_lblPressure);

        //internals
        _lblInternals = new RichTextLabel
            { MinSize = new Vector2(200, 0), VerticalAlignment = VAlignment.Center };
        _btnInternals = new Button { Text = Loc.GetString("gas-tank-window-internals-toggle-button") };

        contentContainer.AddChild(
            new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                Margin = new Thickness(0, 7, 0, 0),
                Children = { _lblInternals, _btnInternals }
            });

        // Separator
        contentContainer.AddChild(new Control
        {
            MinSize = new Vector2(0, 10)
        });

        contentContainer.AddChild(new Label
        {
            Text = Loc.GetString("gas-tank-window-output-pressure-label"),
            Align = Label.AlignMode.Center
        });
        _spbPressure = new FloatSpinBox
        {
            IsValid = f => f >= 0 || f <= 3000,
            Margin = new Thickness(25, 0, 25, 7)
        };
        contentContainer.AddChild(_spbPressure);

        // Handlers
        _spbPressure.OnValueChanged += args =>
        {
            OnOutputPressure?.Invoke(args.Value);
        };

        _btnInternals.OnPressed += args =>
        {
            OnToggleInternals?.Invoke();
        };

    }

    public void SetTitle(string name)
    {
        Title = name;
    }

    public void UpdateState(GasTankBoundUserInterfaceState state)
    {
        _lblPressure.SetMarkup(Loc.GetString("gas-tank-window-tank-pressure-text", ("tankPressure", $"{state.TankPressure:0.##}")));
    }

    public void Update(bool canConnectInternals, bool internalsConnected, float outputPressure)
    {
        _btnInternals.Disabled = !canConnectInternals;
        _lblInternals.SetMarkup(Loc.GetString("gas-tank-window-internal-text",
            ("status", Loc.GetString(internalsConnected ? "gas-tank-window-internal-connected" : "gas-tank-window-internal-disconnected"))));
        _spbPressure.Value = outputPressure;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // Easier than managing state on any ent changes. Previously this was just ticked on server's GasTankSystem.
        if (_entManager.TryGetComponent(Entity, out GasTankComponent? tank))
        {
            var canConnectInternals = _entManager.System<SharedGasTankSystem>().CanConnectToInternals((Entity, tank));
            _btnInternals.Disabled = !canConnectInternals;
        }

        if (!_btnInternals.Disabled)
        {
            _btnInternals.Disabled = _entManager.System<UseDelaySystem>().IsDelayed(Entity, id: SharedGasTankSystem.GasTankDelay);
        }
    }

}
