using Content.Shared._WH40K.MarkingGlow;

namespace Content.Client._WH40K.CharacterCreation;

public sealed partial class Wh40kAppearancePage
{
    private bool MarkingGlowEnabled => _cfg.GetCVar(Wh40kMarkingGlowCVars.Enabled);
    private bool _suppressGlowEvents;

    private void InitializeMarkingGlow()
    {
        HairStylePicker.ConfigureGlow("wh40k-marking-glow-hair", "wh40k-marking-glow-hair-tooltip");
        FacialHairPicker.ConfigureGlow("wh40k-marking-glow-facial-hair", "wh40k-marking-glow-hair-tooltip");

        AppearanceMarkings.IsGlowing = id => _draft?.Profile.Glows(id) == true;
        AppearanceMarkings.OnGlowChanged += SetMarkingGlow;
        AppearanceMarkings.OnMarkingRemoved += _ => PruneMarkingGlow();

        SpeechStylePicker.EyeGlowCheckBox.OnToggled += args =>
        {
            if (!_suppressGlowEvents)
                SetMarkingGlow(Wh40kMarkingGlowRules.Eyes, args.Pressed);
        };
        HairStylePicker.OnGlowChanged += on => SetMarkingGlow(Wh40kMarkingGlowRules.Hair, on);
        FacialHairPicker.OnGlowChanged += on => SetMarkingGlow(Wh40kMarkingGlowRules.FacialHair, on);

        _cfg.OnValueChanged(Wh40kMarkingGlowCVars.Enabled, OnMarkingGlowEnabledChanged);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cfg.UnsubValueChanged(Wh40kMarkingGlowCVars.Enabled, OnMarkingGlowEnabledChanged);

        base.Dispose(disposing);
    }

    private void OnMarkingGlowEnabledChanged(bool _)
    {
        UpdateMarkingGlow();
    }

    private void UpdateMarkingGlow()
    {
        var enabled = MarkingGlowEnabled;
        AppearanceMarkings.IsGlowing = enabled ? id => _draft?.Profile.Glows(id) == true : null;
        AppearanceMarkings.RefreshGlowControls();

        _suppressGlowEvents = true;
        SpeechStylePicker.EyeGlowCheckBox.Visible = enabled;
        SpeechStylePicker.EyeGlowCheckBox.Pressed = _draft?.Profile.Glows(Wh40kMarkingGlowRules.Eyes) == true;
        _suppressGlowEvents = false;

        UpdateHairGlow();
    }

    private void UpdateHairGlow()
    {
        var enabled = MarkingGlowEnabled;
        HairStylePicker.SetGlowState(enabled, _draft?.Profile.Glows(Wh40kMarkingGlowRules.Hair) == true);
        FacialHairPicker.SetGlowState(enabled, _draft?.Profile.Glows(Wh40kMarkingGlowRules.FacialHair) == true);
    }

    private void SetMarkingGlow(string entry, bool on)
    {
        if (_loadingControls || _draft is null || _draft.Profile.Glows(entry) == on)
            return;

        UpdateProfile(profile => profile.WithMarkingGlow(entry, on));
        AppearanceMarkings.RefreshGlowControls();
    }

    private void PruneMarkingGlow()
    {
        if (_draft is null)
            return;

        var kept = Wh40kMarkingGlowRules.Clean(_draft.Profile.MarkingGlow, _draft.Profile.Appearance.Markings);
        if (kept.Count != _draft.Profile.MarkingGlow.Count)
            UpdateProfile(profile => profile.WithMarkingGlowList(kept));
    }
}
