using Content.Shared._WH40K.Synthetics;
using Content.Shared.CCVar;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;

namespace Content.Client._WH40K.Synthetics;

[UsedImplicitly]
public sealed partial class Wh40kChassisBayOverlaySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;

    private Wh40kChassisBayOverlay _overlay = default!;
    private bool _added;
    private bool _reducedMotion;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new Wh40kChassisBayOverlay();
        Subs.CVar(_cfg, CCVars.ReducedMotion, value => _reducedMotion = value, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        SetOverlay(false);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var bay = false;
        var overclock = false;
        if (_player.LocalEntity is { } local && TryComp<Wh40kChassisComponent>(local, out var chassis))
        {
            bay = chassis.BayOpen;
            overclock = chassis.Overclocked;
        }

        Approach(ref _overlay.BayAlpha, bay ? 1f : 0f, frameTime);
        Approach(ref _overlay.OverclockAlpha, overclock ? 1f : 0f, frameTime);
        _overlay.Pulse = _reducedMotion ? MathF.PI / 2f : (_overlay.Pulse + frameTime * 2.2f) % (MathF.PI * 2f);

        if (bay || overclock || _overlay.BayAlpha > 0f || _overlay.OverclockAlpha > 0f)
        {
            if (_overlay.BayText.Length == 0)
            {
                _overlay.BayText = Loc.GetString("wh40k-chassis-ui-bay-banner");
                _overlay.OverclockText = Loc.GetString("wh40k-chassis-ui-overclock-tag");
            }

            SetOverlay(true);
        }
        else
        {
            SetOverlay(false);
        }
    }

    private static void Approach(ref float value, float target, float frameTime)
    {
        value += (target - value) * Math.Min(1f, frameTime * 6f);
        if (MathF.Abs(target - value) < 0.004f)
            value = target;
    }

    private void SetOverlay(bool on)
    {
        if (on == _added)
            return;

        _added = on;
        if (on)
            _overlays.AddOverlay(_overlay);
        else
            _overlays.RemoveOverlay(_overlay);
    }
}
