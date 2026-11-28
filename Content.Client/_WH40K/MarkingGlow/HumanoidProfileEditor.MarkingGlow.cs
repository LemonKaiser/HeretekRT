using Content.Shared._WH40K.MarkingGlow;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private bool MarkingGlowEnabled => _cfgManager.GetCVar(Wh40kMarkingGlowCVars.Enabled);
    private bool _suppressGlowEvents;

    private void InitializeMarkingGlow()
    {
        HairStylePicker.ConfigureGlow("wh40k-marking-glow-hair", "wh40k-marking-glow-hair-tooltip");
        FacialHairPicker.ConfigureGlow("wh40k-marking-glow-facial-hair", "wh40k-marking-glow-hair-tooltip");

        Markings.IsGlowing = id => Profile?.Glows(id) == true;
        Markings.OnGlowChanged += SetMarkingGlow;
        Markings.OnMarkingRemoved += _ => PruneMarkingGlow();

        SpeechStylePicker.EyeGlowCheckBox.OnToggled += args =>
        {
            if (!_suppressGlowEvents)
                SetMarkingGlow(Wh40kMarkingGlowRules.Eyes, args.Pressed);
        };
        HairStylePicker.OnGlowChanged += on => SetMarkingGlow(Wh40kMarkingGlowRules.Hair, on);
        FacialHairPicker.OnGlowChanged += on => SetMarkingGlow(Wh40kMarkingGlowRules.FacialHair, on);

        _cfgManager.OnValueChanged(Wh40kMarkingGlowCVars.Enabled, OnMarkingGlowEnabledChanged);
    }

    private void DisposeMarkingGlow()
    {
        _cfgManager.UnsubValueChanged(Wh40kMarkingGlowCVars.Enabled, OnMarkingGlowEnabledChanged);
    }

    private void OnMarkingGlowEnabledChanged(bool _)
    {
        UpdateMarkingGlow();
    }

    private void UpdateMarkingGlow()
    {
        var enabled = MarkingGlowEnabled;
        Markings.IsGlowing = enabled ? id => Profile?.Glows(id) == true : null;
        Markings.RefreshGlowControls();

        _suppressGlowEvents = true;
        SpeechStylePicker.EyeGlowCheckBox.Visible = enabled;
        SpeechStylePicker.EyeGlowCheckBox.Pressed = Profile?.Glows(Wh40kMarkingGlowRules.Eyes) == true;
        _suppressGlowEvents = false;

        UpdateHairGlow();
    }

    private void UpdateHairGlow()
    {
        var enabled = MarkingGlowEnabled;
        HairStylePicker.SetGlowState(enabled, Profile?.Glows(Wh40kMarkingGlowRules.Hair) == true);
        FacialHairPicker.SetGlowState(enabled, Profile?.Glows(Wh40kMarkingGlowRules.FacialHair) == true);
    }

    private void SetMarkingGlow(string entry, bool on)
    {
        if (IsWh40kAppearanceEditingLocked || Profile == null || Profile.Glows(entry) == on)
            return;

        Profile = Profile.WithMarkingGlow(entry, on);
        SetDirty();
        ReloadProfilePreview();
        Markings.RefreshGlowControls();
    }

    private void PruneMarkingGlow()
    {
        if (Profile == null)
            return;

        var kept = Wh40kMarkingGlowRules.Clean(Profile.MarkingGlow, Profile.Appearance.Markings);
        if (kept.Count != Profile.MarkingGlow.Count)
            Profile.MarkingGlow = kept;
    }
}
