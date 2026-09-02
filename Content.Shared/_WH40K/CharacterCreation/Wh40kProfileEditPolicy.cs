using Content.Shared.Humanoid;
using Content.Shared.Preferences;

namespace Content.Shared._WH40K.CharacterCreation;

public enum Wh40kProfileEditMode : byte
{
    Disabled,
    AppearanceLocked,
    FullLocked,
}

/// <summary>
/// Shared parsing and profile-field policy for the WH40K profile edit CVars.
/// </summary>
public static class Wh40kProfileEditPolicy
{
    public static Wh40kProfileEditMode ParseMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "appearance" => Wh40kProfileEditMode.AppearanceLocked,
            "full" => Wh40kProfileEditMode.FullLocked,
            _ => Wh40kProfileEditMode.Disabled,
        };
    }

    /// <summary>
    /// Keeps the immutable character identity and visual appearance while retaining editable preferences.
    /// </summary>
    public static HumanoidCharacterProfile PreserveIdentityAndAppearance(
        HumanoidCharacterProfile original,
        HumanoidCharacterProfile changed)
    {
        return changed
            .WithName(original.Name)
            .WithAge(original.Age)
            .WithSex(original.Sex)
            .WithGender(original.Gender)
            .WithSpecies(original.Species)
            .WithCharacterAppearance(new HumanoidCharacterAppearance(original.Appearance));
    }
}
