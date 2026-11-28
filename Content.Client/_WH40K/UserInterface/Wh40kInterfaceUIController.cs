using System.Linq;
using System.Numerics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Content.Client._WH40K.CharacterCreation;
using Content.Client.Options.UI;
using Content.Client.PDA;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Client._WH40K.UserInterface.WindowPopout;

namespace Content.Client._WH40K.UserInterface;

public sealed class Wh40kInterfaceUIController : UIController
{
    private const string Applied = "Wh40kHeritageWindow";
    [Dependency] private IResourceCache _resources = default!;
    internal Wh40kGlassPass Glass { get; private set; } = default!;
    private readonly HashSet<Control> _floatingPanels = [];
    internal bool HasVisibleFloatingPanels => _floatingPanels.Any(panel => panel.VisibleInTree);
    internal void RegisterFloatingPanel(Control panel) => _floatingPanels.Add(panel);
    internal void UnregisterFloatingPanel(Control panel) => _floatingPanels.Remove(panel);

    public override void Initialize()
    {
        Glass = new Wh40kGlassPass();
        // HUD surfaces render in StateRoot, so prepare or release their shared texture before that tree draws.
        UIManager.RootControl.AddChild(Glass);
        Glass.SetPositionFirst();
        UIManager.WindowRoot.OnChildAdded += OnWindowAdded;
        UIManager.ModalRoot.OnChildAdded += OnWindowAdded;
    }

    private void OnWindowAdded(Control control)
    {
        if (control is Popup && control.HasStyleClass(OptionButton.StyleClassPopup))
        {
            foreach (var button in Descendants(control).OfType<Button>()) button.AddStyleClass("Wh40kMenuItem");
            Wh40kInterfaceTheme.ApplyControls(control);
        }
        if (control is not BaseWindow || control.HasStyleClass(Applied))
            return;
        control.AddStyleClass(Applied);
        if (control is RadialMenu)
            return;
        var available = Wh40kWindowViewport.AvailableSize(UIManager.RootControl.Size);
        if (control is FancyWindow large &&
            (large.DesiredSize.X > available.X || large.DesiredSize.Y > available.Y))
            large.ContentsContainer.AddChild(new Wh40kWindowViewport(large));
        foreach (var button in Descendants(control).OfType<TextureButton>().Where(button =>
                     button.HasStyleClass(DefaultWindow.StyleClassWindowCloseButton) || button.HasStyleClass(FancyWindow.StyleClassWindowHelpButton)))
        {
            button.Scale = new Vector2(0.375f);
            button.Margin = new Thickness(1);
            button.Modulate = Color.White;
        }

        var title = Descendants(control).OfType<Label>().FirstOrDefault(label =>
            label.HasStyleClass(DefaultWindow.StyleClassWindowTitle) || label.HasStyleClass("FancyWindowTitle"));
        if (title?.Parent is PanelContainer panel)
        {
            panel.RemoveChild(title);
            panel.AddChild(new Wh40kWindowTitle(title, _resources));
        }
        else if (title?.Parent is BoxContainer row)
        {
            var buttons = row.Children.Where(child => child != title).ToArray();
            foreach (var button in buttons.OfType<TextureButton>())
            {
                button.Scale = new Vector2(0.375f);
                button.Margin = new Thickness(1);
            }
            var minimize = new Wh40kCollapseButton
            {
                TextureNormal = _resources.GetResource<TextureResource>("/Textures/_WH40K/Interface/minimize.svg.192dpi.png").Texture,
                Scale = new Vector2(0.375f), Margin = new Thickness(1), VerticalAlignment = Control.VAlignment.Center,
                ToolTip = Loc.GetString("wh40k-window-minimize"),
            };
            row.AddChild(minimize);
            var insertBefore = buttons.FirstOrDefault(child =>
                child is Wh40kPopoutButton ||
                (child is TextureButton tb && tb.HasStyleClass(DefaultWindow.StyleClassWindowCloseButton)));
            minimize.SetPositionInParent(insertBefore != null
                ? insertBefore.GetPositionInParent()
                : Math.Max(0, row.ChildCount - 2));
            if (control is OptionsMenu)
            {
                var help = new TextureButton
                {
                    TextureNormal = _resources.GetResource<TextureResource>("/Textures/_WH40K/Interface/help.svg.192dpi.png").Texture,
                    Scale = new Vector2(0.375f), Margin = new Thickness(1), VerticalAlignment = Control.VAlignment.Center,
                    ToolTip = Loc.GetString("wh40k-ui-help"),
                };
                help.AddStyleClass(Content.Client.UserInterface.Controls.FancyWindow.StyleClassWindowHelpButton);
                help.OnPressed += _ => UIManager.GetUIController<GuidebookUIController>().ToggleGuidebook();
                row.AddChild(help); help.SetPositionInParent(minimize.GetPositionInParent());
            }
            if (row.Parent?.Parent is BoxContainer layout)
            {
                var header = row.Parent;
                var collapsed = false;
                var height = 0f;
                var minHeight = 0f;
                var resizable = false;
                var body = layout.Children.Where(child => child != header).Select(child => (Control: child, Visible: child.Visible)).ToArray();
                minimize.ToggleAction = () =>
                {
                    var window = (BaseWindow) control;
                    if (!collapsed)
                    {
                        height = window.Height; minHeight = window.MinHeight; resizable = window.Resizable;
                        foreach (var item in body) item.Control.Visible = false;
                        window.MinHeight = 0; window.Resizable = false; window.SetHeight = header.Height + 2;
                    }
                    else
                    {
                        foreach (var item in body) item.Control.Visible = item.Visible;
                        window.MinHeight = minHeight; window.Resizable = resizable; window.SetHeight = height;
                    }
                    collapsed = !collapsed;
                    minimize.IsCollapsed = collapsed;
                    minimize.ToolTip = Loc.GetString(collapsed ? "wh40k-window-restore" : "wh40k-window-minimize");
                };
            }
            var position = title.GetPositionInParent();
            row.RemoveChild(title);
            row.AddChild(new Wh40kWindowTitle(title, _resources));
            row.GetChild(row.ChildCount - 1).SetPositionInParent(position);
            row.SeparationOverride = 0;
        }
        if (control is DefaultWindow standard)
        {
            standard.Contents.Margin = new Thickness(10);
            if (standard is Content.Client.Administration.UI.Bwoink.BwoinkWindow || standard.Title == Loc.GetString("view-variables"))
            {
                standard.SetSize = Vector2.Min(new Vector2(1100, 620), available);
                standard.MinSize = Vector2.Min(standard.MinSize, available);
                standard.Measure(available);
            }
        }
        foreach (var tabs in Descendants(control).OfType<TabContainer>().Where(tabs => tabs.TabsVisible).ToArray())
        {
            if (tabs.Parent is not { } parent) continue;
            var position = tabs.GetPositionInParent();
            parent.RemoveChild(tabs);
            var column = new Wh40kTabLayout(tabs);
            parent.AddChild(column); column.SetPositionInParent(position);
        }
        Wh40kInterfaceTheme.ApplyControls(control);
        if (control is DefaultWindow or FancyWindow)
            control.AddChild(new Wh40kWindowTrim(_resources));
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
