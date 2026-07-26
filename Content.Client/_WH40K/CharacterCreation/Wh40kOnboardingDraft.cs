using Content.Shared.Preferences;

namespace Content.Client._WH40K.CharacterCreation;

public sealed class Wh40kOnboardingDraft
{
    public HumanoidCharacterProfile Profile { get; private set; }
    public int TemporaryProfileSlot { get; }
    // The default high-priority job is Wanderer. Keep its existing starting gear visible in the preview
    // until the player explicitly turns clothes off in the appearance section.
    public bool ShowClothes { get; set; } = true;
    public string? PortraitId => Profile.Wh40kBuild.PortraitId;
    public bool PortraitSelected => PortraitId is not null;

    public Wh40kOnboardingDraft(HumanoidCharacterProfile profile, int temporaryProfileSlot)
    {
        Profile = profile.Clone();
        TemporaryProfileSlot = temporaryProfileSlot;
    }

    public void UpdateProfile(HumanoidCharacterProfile profile)
    {
        Profile = profile;
    }

    public void SelectPortrait(string portraitId)
    {
        var updatedBuild = Profile.Wh40kBuild.Clone();
        updatedBuild.PortraitId = portraitId;
        UpdateProfile(Profile.WithWh40kCharacterBuild(updatedBuild));
    }

    public void ClearPortrait()
    {
        var updatedBuild = Profile.Wh40kBuild.Clone();
        updatedBuild.PortraitId = null;
        UpdateProfile(Profile.WithWh40kCharacterBuild(updatedBuild));
    }
}
