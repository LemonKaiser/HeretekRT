using Content.Shared.Durability;
using Content.Shared.Durability.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client.Durability.Controls;

/// <summary>
/// Draws the durability of an entity over an item UI slot.
/// </summary>
public sealed class DurabilityBarControl : Control
{
    private const float HorizontalMargin = 6f;
    private const float BottomMargin = 4f;
    private const float BarHeight = 7f;

    private static readonly Color FrameColor = Color.FromHex("#395158");
    private static readonly Color TrackColor = Color.FromHex("#071012");
    private static readonly Color ShadowColor = Color.Black.WithAlpha(0.75f);
    private static readonly Color WhiteFillColor = Color.FromHex("#E8F1F2");
    private static readonly Color LowFillColor = Color.FromHex("#D4473F");
    private static readonly Color MiddleFillColor = Color.FromHex("#D9A441");
    private static readonly Color HighFillColor = Color.FromHex("#55B96B");

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;

    private EntityUid? _entity;
    private float _progress;
    private bool _drawBar;
    private DurabilityBarColor _colorMode;

    /// <summary>
    /// Whether this control belongs to a real hand slot. Used by the HeldOnly preference.
    /// </summary>
    public bool HeldSlot { get; set; }

    public DurabilityBarControl()
    {
        IoCManager.InjectDependencies(this);

        MouseFilter = MouseFilterMode.Ignore;
        HorizontalAlignment = HAlignment.Stretch;
        VerticalAlignment = VAlignment.Bottom;
        Margin = new Thickness(HorizontalMargin, 0f, HorizontalMargin, BottomMargin);
        SetHeight = BarHeight;
    }

    public void SetEntity(EntityUid? entity)
    {
        _entity = entity;

        if (entity == null)
            _drawBar = false;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateBarState();
    }

    /// <summary>
    /// Draws one logical durability bar across the supplied storage-shape
    /// fragments. A non-rectangular item can therefore wrap its indicator
    /// around empty cells without duplicating its durability in every piece.
    /// </summary>
    public void DrawStorageBars(
        DrawingHandleScreen handle,
        IReadOnlyList<UIBox2> storageAreas,
        float uiScale)
    {
        UpdateBarState();

        if (!_drawBar || storageAreas.Count == 0)
            return;

        var totalTrackWidth = 0f;
        foreach (var storageArea in storageAreas)
        {
            var barArea = GetStorageBarArea(storageArea, uiScale);
            totalTrackWidth += GetTrackWidth(barArea, uiScale);
        }

        if (totalTrackWidth <= 0f)
            return;

        var remainingFillWidth = _progress * totalTrackWidth;
        foreach (var storageArea in storageAreas)
        {
            var barArea = GetStorageBarArea(storageArea, uiScale);
            var trackWidth = GetTrackWidth(barArea, uiScale);
            if (trackWidth <= 0f)
                continue;

            var fragmentProgress = Math.Clamp(remainingFillWidth / trackWidth, 0f, 1f);
            // The logical path visits right-hand fragments first, but each
            // physical fragment retains the familiar left-to-right fill used
            // by durability bars in hands and ordinary inventory slots.
            DrawBar(handle, barArea, uiScale, fragmentProgress);
            remainingFillWidth -= trackWidth;
        }
    }

    private void UpdateBarState()
    {
        var visibility = (DurabilityBarVisibility) _configuration.GetCVar(DurabilityCVars.BarVisibility);
        if (visibility == DurabilityBarVisibility.Never ||
            visibility == DurabilityBarVisibility.HeldOnly && !HeldSlot)
        {
            _drawBar = false;
            return;
        }

        if (_entity is not { } entity ||
            !_entityManager.TryGetComponent(entity, out ItemDurabilityComponent? durability) ||
            !float.IsFinite(durability.MaxDurability) ||
            DurabilityMath.Round(durability.MaxDurability) <= 0f)
        {
            _drawBar = false;
            return;
        }

        var current = durability.CurrentDurability < 0f
            ? durability.MaxDurability
            : durability.CurrentDurability;
        current = DurabilityMath.Clamp(current, durability.MaxDurability);
        var maximum = DurabilityMath.Round(durability.MaxDurability);

        _progress = Math.Clamp(current / maximum, 0f, 1f);
        _colorMode = (DurabilityBarColor) _configuration.GetCVar(DurabilityCVars.BarColor);
        _drawBar = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        DrawBar(handle, PixelSizeBox, UIScale, _progress);
    }

    private static UIBox2 GetStorageBarArea(UIBox2 storageArea, float uiScale)
    {
        var horizontalMargin = HorizontalMargin * uiScale;
        var bottomMargin = BottomMargin * uiScale;
        var height = BarHeight * uiScale;
        return new UIBox2(
            storageArea.Left + horizontalMargin,
            storageArea.Bottom - bottomMargin - height,
            storageArea.Right - horizontalMargin,
            storageArea.Bottom - bottomMargin);
    }

    private static float GetTrackWidth(UIBox2 area, float uiScale)
    {
        var pixel = MathF.Max(1f, MathF.Round(uiScale));
        return MathF.Max(0f, area.Width - pixel * 2f);
    }

    private void DrawBar(
        DrawingHandleScreen handle,
        UIBox2 area,
        float uiScale,
        float progress)
    {
        if (!_drawBar || area.Width <= 3f || area.Height <= 3f)
            return;

        var pixel = MathF.Max(1f, MathF.Round(uiScale));
        var shadow = new UIBox2(area.Left, area.Top + pixel, area.Right, area.Bottom);
        var frame = new UIBox2(area.Left, area.Top, area.Right, area.Bottom - pixel);
        var track = new UIBox2(
            frame.Left + pixel,
            frame.Top + pixel,
            frame.Right - pixel,
            frame.Bottom - pixel);

        if (track.Width <= 0f || track.Height <= 0f)
            return;

        handle.DrawRect(shadow, ShadowColor);
        handle.DrawRect(frame, FrameColor);
        handle.DrawRect(track, TrackColor);

        var trackWidth = track.Size.X;
        var fillWidth = MathF.Round(trackWidth * progress);
        if (fillWidth > 0f)
        {
            var fillColor = GetFillColor();
            var fill = new UIBox2(track.Left, track.Top, track.Left + fillWidth, track.Bottom);
            handle.DrawRect(fill, fillColor);

            var highlightHeight = MathF.Min(pixel, fill.Size.Y);
            var highlight = new UIBox2(fill.Left, fill.Top, fill.Right, fill.Top + highlightHeight);
            handle.DrawRect(highlight, Color.InterpolateBetween(fillColor, Color.White, 0.28f));
        }

        // Thin divisions give the bar its own compact technical style and keep it readable at a glance.
        for (var i = 1; i < 5; i++)
        {
            var x = track.Left + trackWidth * i / 5f;
            var divider = new UIBox2(x, track.Top, x + MathF.Max(1f, pixel * 0.5f), track.Bottom);
            handle.DrawRect(divider, Color.Black.WithAlpha(0.32f));
        }
    }

    private Color GetFillColor()
    {
        if (_colorMode == DurabilityBarColor.White)
            return WhiteFillColor;

        return _progress < 0.5f
            ? Color.InterpolateBetween(LowFillColor, MiddleFillColor, _progress * 2f)
            : Color.InterpolateBetween(MiddleFillColor, HighFillColor, (_progress - 0.5f) * 2f);
    }
}
