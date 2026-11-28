using System.Numerics;
using Content.Shared.Humanoid;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.CharacterCreation;

public sealed class Wh40kSexPicker : BoxContainer
{
    private bool _disabled;

    public event Action<Sex>? OnSelected;

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            foreach (var child in Children)
                ((Button) child).Disabled = value;
        }
    }

    public Wh40kSexPicker()
    {
        SeparationOverride = 5;
    }

    public void SetOptions(IEnumerable<Sex> options, Sex selected)
    {
        RemoveAllChildren();
        var group = new ButtonGroup();
        var font = Wh40kOnboardingTypography.Text(IoCManager.Resolve<IResourceCache>(), 18);
        foreach (var option in options)
        {
            var (icon, key) = option switch
            {
                Sex.Male => ("♂", "humanoid-profile-editor-sex-male-text"),
                Sex.Female => ("♀", "humanoid-profile-editor-sex-female-text"),
                _ => ("⚲", "humanoid-profile-editor-sex-unsexed-text"),
            };
            var button = new Button
            {
                Text = icon,
                MinSize = new Vector2(42, 36),
                ToolTip = Loc.GetString(key),
                ToggleMode = true,
                Group = group,
                Pressed = selected == option,
                Disabled = _disabled,
            };
            button.Label.FontOverride = font;
            button.OnPressed += _ => OnSelected?.Invoke(option);
            AddChild(button);
        }
    }
}
