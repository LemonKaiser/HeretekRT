using System.Numerics;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Screens;

/// <summary>
///     Screens that are considered to be 'in-game'.
/// </summary>
public abstract class InGameScreen : UIScreen
{
    public bool GameplayHudVisible { get; protected set; } = true;

    public void AttachBossHud(Control control)
    {
        var viewport = GetWidget<Content.Client.UserInterface.Controls.MainViewport>();
        Control parent = viewport?.Parent ?? this;
        if (control.Parent == parent)
            return;
        control.Orphan();
        parent.AddChild(control);
        SetAnchorPreset(control, LayoutPreset.Wide);
    }

    public Action<Vector2>? OnChatResized;

    public abstract ChatBox ChatBox { get; }

    public abstract void SetChatSize(Vector2 size);

    public abstract void SetHudVisible(bool visible);

    /// <summary>
    /// Hides every gameplay HUD element, including the top bar. Normal dialogue hiding intentionally keeps that
    /// bar available, while cinematic scenes such as Act I require a completely clean screen.
    /// </summary>
    public virtual void SetHudVisibleFully(bool visible)
    {
        SetHudVisible(visible);
    }

    public abstract void AttachDialogueOverlay(Control overlay);
}
