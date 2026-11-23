using System.Linq;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;

namespace Content.Client._WH40K.UserInterface.WindowPopout;

/// <summary>
/// Adds a pop-out button next to the close button of every in-game window, letting players drag windows onto
/// another monitor. Works from outside the window classes, so no window needs to opt in.
/// </summary>
public sealed partial class Wh40kWindowPopoutUIController : UIController, IOnStateExited<GameplayState>
{
    [Dependency] private IConfigurationManager _cfg = default!;

    // Header depth of FancyWindow, DefaultWindow and the custom PDA-style windows, with a little slack.
    private const int CloseButtonSearchDepth = 5;

    private readonly Dictionary<BaseWindow, Wh40kPopoutWindow> _popouts = new();
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _cfg.OnValueChanged(CCVars.UIWindowPopout, OnWindowPopoutChanged, true);
        UIManager.WindowRoot.OnChildAdded += OnWindowAdded;
    }

    private void OnWindowPopoutChanged(bool value)
    {
        _enabled = value;

        foreach (var child in UIManager.WindowRoot.Children)
        {
            if (child is BaseWindow window)
                UpdateButtonVisibility(window);
        }

        foreach (var window in _popouts.Keys)
        {
            UpdateButtonVisibility(window);
        }
    }

    private void OnWindowAdded(Control child)
    {
        if (child is not BaseWindow window || window is RadialMenu)
            return;

        if (FindCloseButton(window) is not { Parent: { } header } close)
            return;

        var button = header.Children.OfType<Wh40kPopoutButton>().FirstOrDefault();
        if (button == null)
        {
            button = new Wh40kPopoutButton();
            button.OnPressed += _ => Toggle(window);
            header.AddChild(button);
            button.SetPositionInParent(close.GetPositionInParent());
        }

        button.PoppedOut = _popouts.ContainsKey(window);
        button.Visible = _enabled || button.PoppedOut;
    }

    public bool IsPoppedOut(BaseWindow window)
    {
        return _popouts.ContainsKey(window);
    }

    public void OnStateExited(GameplayState state)
    {
        ClosePopouts();
    }

    public void ClosePopouts()
    {
        foreach (var popout in _popouts.Values.ToArray())
        {
            popout.Close();
        }
    }

    /// <summary>
    /// Pops an open window out into its own OS window, or docks a popped-out one back into the game window.
    /// </summary>
    public void Toggle(BaseWindow window)
    {
        if (_popouts.TryGetValue(window, out var existing))
        {
            existing.Dock();
            return;
        }

        if (!_enabled || !window.IsOpen)
            return;

        var popout = new Wh40kPopoutWindow(window);
        popout.Closed += () =>
        {
            _popouts.Remove(window);
            SetButtonState(window, false);
        };

        popout.PopOut();
        _popouts[window] = popout;
        SetButtonState(window, true);
    }

    private void UpdateButtonVisibility(BaseWindow window)
    {
        var button = FindCloseButton(window)?.Parent?.Children.OfType<Wh40kPopoutButton>().FirstOrDefault();
        if (button != null)
            button.Visible = _enabled || button.PoppedOut;
    }

    private void SetButtonState(BaseWindow window, bool poppedOut)
    {
        var button = FindCloseButton(window)?.Parent?.Children.OfType<Wh40kPopoutButton>().FirstOrDefault();
        if (button == null)
            return;

        button.PoppedOut = poppedOut;
        button.Visible = _enabled || poppedOut;
    }

    /// <summary>
    /// Breadth-first, so the title bar's close button wins over any close-styled button in the contents.
    /// </summary>
    private static TextureButton? FindCloseButton(Control window)
    {
        var queue = new Queue<(Control Control, int Depth)>();
        queue.Enqueue((window, 0));

        while (queue.TryDequeue(out var entry))
        {
            if (entry.Control is TextureButton button && button.HasStyleClass(DefaultWindow.StyleClassWindowCloseButton))
                return button;

            if (entry.Depth >= CloseButtonSearchDepth)
                continue;

            foreach (var child in entry.Control.Children)
            {
                queue.Enqueue((child, entry.Depth + 1));
            }
        }

        return null;
    }
}
