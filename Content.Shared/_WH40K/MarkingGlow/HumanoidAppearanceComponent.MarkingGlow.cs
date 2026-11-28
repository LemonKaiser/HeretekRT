namespace Content.Shared.Humanoid;

public sealed partial class HumanoidAppearanceComponent
{
    /// <summary>
    /// Marking IDs and special keys (@eyes, @hair, @facial_hair) drawn unshaded on this humanoid.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<string> MarkingGlow = new();
}
