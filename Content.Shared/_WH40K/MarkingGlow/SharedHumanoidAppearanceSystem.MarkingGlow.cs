using Content.Shared._WH40K.MarkingGlow;
using Content.Shared.Preferences;

namespace Content.Shared.Humanoid;

public abstract partial class SharedHumanoidAppearanceSystem
{
    protected bool MarkingGlowEnabled => _cfgManager.GetCVar(Wh40kMarkingGlowCVars.Enabled);

    protected List<string> ProfileMarkingGlow(HumanoidCharacterProfile profile)
    {
        return MarkingGlowEnabled
            ? Wh40kMarkingGlowRules.Clean(profile.MarkingGlow)
            : new List<string>();
    }
}
