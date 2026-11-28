using Content.Shared._WH40K.MarkingGlow;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Humanoid;

public sealed partial class MarkingPicker
{
    /// <summary>
    /// Returns whether a marking ID currently glows, or null when marking glow is disabled.
    /// </summary>
    public Func<string, bool>? IsGlowing { get; set; }

    /// <summary>
    /// Raised when the user toggles glow for an applied marking.
    /// </summary>
    public event Action<string, bool>? OnGlowChanged;

    private CheckBox? _galleryGlowCheckBox;

    public void RefreshGlowControls()
    {
        if (UseTiles)
            PopulateGallerySide();
    }
}
