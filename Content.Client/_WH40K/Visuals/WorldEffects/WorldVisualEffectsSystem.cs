using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client._WH40K.Visuals.WorldEffects;

public sealed partial class WorldVisualEffectsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IOverlayManager _overlays = default!;

    private ConfigurationMultiSubscriptionBuilder _subscription = default!;
    private LightSourceGlowOverlay? _sourceGlow;
    private ContactShadowOverlay? _shadows;
    private PuddleReflectionOverlay? _puddleReflections;

    public override void Initialize()
    {
        base.Initialize();
        _subscription = _config.SubscribeMultiple()
            .OnValueChanged(CCVars.WH40KWorldGlow, _ => UpdateGlow())
            .OnValueChanged(CCVars.WH40KContactShadows, _ => UpdateShadows())
            .OnValueChanged(CCVars.WH40KPuddleReflections, _ => UpdateReflections());
        UpdateGlow();
        UpdateShadows();
        UpdateReflections();
    }

    private void UpdateGlow()
    {
        var enabled = _config.GetCVar(CCVars.WH40KWorldGlow);

        // The overlay subscribes to IResourceCache.OnRsiLoaded and keeps a per-RSI geometry cache, so toggling
        // the option must only flip the Enabled flag instead of recreating the overlay.
        if (_sourceGlow == null)
        {
            _sourceGlow = new LightSourceGlowOverlay();
            _overlays.AddOverlay(_sourceGlow);
        }

        _sourceGlow.Enabled = enabled;
    }

    private void UpdateShadows()
    {
        if (!_config.GetCVar(CCVars.WH40KContactShadows))
        {
            RemoveShadows();
            return;
        }

        if (_shadows == null)
        {
            _shadows = new ContactShadowOverlay();
            _overlays.AddOverlay(_shadows);
        }
    }

    private void RemoveGlow()
    {
        if (_sourceGlow == null)
            return;

        _overlays.RemoveOverlay(_sourceGlow);
        _sourceGlow.Dispose();
        _sourceGlow = null;
    }

    private void RemoveShadows()
    {
        if (_shadows == null)
            return;

        _overlays.RemoveOverlay(_shadows);
        _shadows.Dispose();
        _shadows = null;
    }

    private void UpdateReflections()
    {
        if (!_config.GetCVar(CCVars.WH40KPuddleReflections))
        {
            RemoveReflections();
            return;
        }

        if (_puddleReflections != null)
            return;

        _puddleReflections = new PuddleReflectionOverlay();
        _overlays.AddOverlay(_puddleReflections);
    }

    private void RemoveReflections()
    {
        if (_puddleReflections == null)
            return;

        _overlays.RemoveOverlay(_puddleReflections);
        _puddleReflections.Dispose();
        _puddleReflections = null;
    }

    public override void Shutdown()
    {
        _subscription.Dispose();
        RemoveGlow();
        RemoveShadows();
        RemoveReflections();
        base.Shutdown();
    }
}
