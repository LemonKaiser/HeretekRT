using Content.Shared._WH40K.HeatHaze;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client._WH40K.HeatHaze;

/// <summary>
/// Keeps <see cref="Wh40kHeatHazeOverlay"/> up while heat haze is enabled and reduced motion is off.
/// </summary>
public sealed partial class Wh40kHeatHazeSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IOverlayManager _overlayMan = default!;

    private Wh40kHeatHazeOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new Wh40kHeatHazeOverlay();

        Subs.CVar(_cfg, Wh40kHeatHazeCVars.Strength, value => _overlay.Strength = Math.Max(value, 0f), true);
        Subs.CVar(_cfg, Wh40kHeatHazeCVars.Enabled, _ => UpdateOverlay());
        Subs.CVar(_cfg, CCVars.ReducedMotion, _ => UpdateOverlay(), true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayMan.RemoveOverlay(_overlay);
        _overlay.Dispose();
    }

    private void UpdateOverlay()
    {
        if (_cfg.GetCVar(Wh40kHeatHazeCVars.Enabled) && !_cfg.GetCVar(CCVars.ReducedMotion))
            _overlayMan.AddOverlay(_overlay);
        else
            _overlayMan.RemoveOverlay(_overlay);
    }
}
