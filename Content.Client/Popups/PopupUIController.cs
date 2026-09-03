using System.Numerics;
using Content.Client.Gameplay;
using Content.Shared.Popups;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client.Popups;

/// <summary>
/// Handles screens-space popups. World popups are handled via PopupOverlay.
/// </summary>
public sealed class PopupUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [UISystemDependency] private readonly PopupSystem? _popup = default!;

    private Font _smallFont = default!;
    private Font _mediumFont = default!;
    private Font _largeFont = default!;

    private PopupRootControl? _popupControl;

    public override void Initialize()
    {
        base.Initialize();
        var cache = IoCManager.Resolve<IResourceCache>();

        _smallFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Italic.ttf"), 10);
        _mediumFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Italic.ttf"), 12);
        _largeFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-BoldItalic.ttf"), 14);
    }

    public void OnStateEntered(GameplayState state)
    {
        _popupControl = new PopupRootControl(_popup, this);

        UIManager.RootControl.AddChild(_popupControl);
    }

    public void OnStateExited(GameplayState state)
    {
        if (_popupControl == null)
            return;

        UIManager.RootControl.RemoveChild(_popupControl);
        _popupControl = null;
    }

    public void DrawPopup(PopupSystem.PopupLabel popup, DrawingHandleScreen handle, Vector2 position, float scale)
    {
        var font = _smallFont;
        var color = Color.White;

        switch (popup.Type)
        {
            case PopupType.SmallCaution:
                color = Color.Red;
                break;
            case PopupType.Medium:
                font = _mediumFont;
                color = Color.LightGray;
                break;
            case PopupType.MediumCaution:
                font = _mediumFont;
                color = Color.Red;
                break;
            case PopupType.Large:
                font = _largeFont;
                color = Color.LightGray;
                break;
            case PopupType.LargeCaution:
                font = _largeFont;
                color = Color.Red;
                break;
        }

        if (popup is PopupSystem.WorldPopupLabel worldPopup)
            worldPopup.PrepareLayout(font, scale);

        var lifetime = PopupSystem.GetPopupLifetime(popup);
        var fadeStart = PopupSystem.GetPopupFadeStart(popup);

        // World text stays opaque until it has finished revealing. Cursor popups retain their old fade behavior.
        var alpha = popup.TotalTime <= fadeStart || fadeStart >= lifetime
            ? 1f
            : Math.Clamp((lifetime - popup.TotalTime) / (lifetime - fadeStart), 0f, 1f);

        var updatedPosition = position - new Vector2(0f, MathF.Min(8f, 12f * (popup.TotalTime * popup.TotalTime + popup.TotalTime)));

        // A progressively revealed world popup must keep its final horizontal center; measuring
        // only the current prefix makes it visibly jump sideways as every letter is added.
        var displayText = popup is PopupSystem.WorldPopupLabel repeatedWorldPopup
            ? repeatedWorldPopup.TextWithRepeatCount
            : popup.Text;
        var measuredText = popup is PopupSystem.WorldPopupLabel preparedWorldPopup
            ? preparedWorldPopup.ReservedTextWithRepeatCount
            : displayText;
        var dimensions = handle.GetDimensions(font, measuredText, scale);
        handle.DrawString(font, updatedPosition - dimensions / 2f, displayText, scale, color.WithAlpha(alpha));
    }

    /// <summary>
    /// Handles drawing all screen popups.
    /// </summary>
    private sealed class PopupRootControl : Control
    {
        private readonly PopupSystem? _popup;
        private readonly PopupUIController _controller;

        public PopupRootControl(PopupSystem? system, PopupUIController controller)
        {
            _popup = system;
            _controller = controller;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            if (_popup == null)
                return;

            // Different window
            var windowId = UserInterfaceManager.RootControl.Window.Id;

            foreach (var popup in _popup.CursorLabels)
            {
                if (popup.InitialPos.Window != windowId)
                    continue;

                _controller.DrawPopup(popup, handle, popup.InitialPos.Position, UIScale);
            }
        }
    }
}
