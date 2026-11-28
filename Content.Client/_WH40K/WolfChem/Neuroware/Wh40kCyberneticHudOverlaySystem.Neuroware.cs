using Content.Shared._WH40K.WolfChem.Narcotics;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Synthetics;

public sealed partial class Wh40kCyberneticHudOverlaySystem
{
    [Dependency] private IPrototypeManager _wh40kNeurowarePrototypes = default!;

    private const float Wh40kNeurowareGlitchAt = 0.6f;

    /// <summary>How hard running neuroware drives the cybernetic HUD readout, 0-1.</summary>
    private float Wh40kNeurowareIntensity(EntityUid local)
    {
        if (!TryComp<Wh40kIntoxicationComponent>(local, out var intox))
            return 0f;

        var intensity = 0f;
        foreach (var (profile, level) in intox.Levels)
        {
            if (!_wh40kNeurowarePrototypes.TryIndex(profile, out var proto))
                continue;

            var tier = proto.TierAt(level);
            if (tier >= 0)
                intensity = MathF.Max(intensity, MathF.Max(proto.Tiers[tier].Overlay, 0.25f));
        }

        foreach (var profile in intox.Crashes.Keys)
        {
            if (_wh40kNeurowarePrototypes.TryIndex(profile, out var proto) && proto.Crash != null)
                intensity = MathF.Max(intensity, proto.Crash.Overlay * 0.5f);
        }

        return Math.Clamp(intensity, 0f, 1f);
    }
}
