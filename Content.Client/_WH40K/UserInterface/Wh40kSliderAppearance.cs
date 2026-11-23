using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.UserInterface;

/// <summary>Slider.Disabled has no style pseudo-class. Update only the tint when that flag changes.</summary>
internal sealed class Wh40kSliderAppearance : Control
{
    private readonly Slider _slider;
    private readonly Color? _original;
    private bool? _disabled;
    internal Wh40kSliderAppearance(Slider slider)
    {
        _slider = slider; _original = slider.ModulateSelfOverride;
        MouseFilter = MouseFilterMode.Ignore;
    }
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_disabled == _slider.Disabled) return;
        _disabled = _slider.Disabled;
        var original = _original ?? _slider.StylePropertyDefault(StylePropertyModulateSelf, Color.White);
        _slider.ModulateSelfOverride = _slider.Disabled ? original.WithAlpha(original.A * 0.38f) : _original;
    }
}
