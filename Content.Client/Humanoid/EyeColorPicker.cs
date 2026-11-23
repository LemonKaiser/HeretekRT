using Content.Client._WH40K.CharacterCreation;
using Robust.Client.UserInterface;

namespace Content.Client.Humanoid;

public sealed class EyeColorPicker : Control
{
    public event Action<Color>? OnEyeColorPicked;

    private readonly Wh40kColorPalette _palette;
    public bool Disabled { set => _palette.SetDisabled(value); }

    public void SetData(Color color)
    {
        _palette.SetColor(color);
    }

    public EyeColorPicker()
    {
        AddChild(_palette = new Wh40kColorPalette(Color.Brown));
        _palette.OnColorChanged += color => OnEyeColorPicked?.Invoke(color);
    }
}
