using Content.Shared.CCVar;
using Content.Shared._WH40K.Visuals.ScreenEffects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._WH40K.Visuals.ScreenEffects;

public sealed class ScreenSaturationSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;

    private ScreenSaturationOverlay _overlay = default!;
    private bool _enabled;

    public override void Initialize()
    {
        _overlay = new ScreenSaturationOverlay();
        _enabled = _config.GetCVar(CCVars.HudScreenSaturationEffects);
        _config.OnValueChanged(CCVars.HudScreenSaturationEffects, OnEnabledChanged);
        SubscribeLocalEvent<ScreenSaturationComponent, ComponentInit>(OnComponentChanged);
        SubscribeLocalEvent<ScreenSaturationComponent, ComponentShutdown>(OnComponentRemoved);
        SubscribeLocalEvent<ScreenSaturationComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<ScreenSaturationComponent, PlayerDetachedEvent>(OnPlayerDetached);
    }

    public override void Shutdown()
    {
        _config.UnsubValueChanged(CCVars.HudScreenSaturationEffects, OnEnabledChanged);
        RemoveOverlay();
    }

    private void OnEnabledChanged(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
            RemoveOverlay();
        else if (_players.LocalEntity is { } player && HasComp<ScreenSaturationComponent>(player))
            AddOverlay();
    }

    private void OnComponentChanged(Entity<ScreenSaturationComponent> ent, ref ComponentInit args)
    {
        if (_enabled && ent.Owner == _players.LocalEntity)
            AddOverlay();
    }

    private void OnComponentRemoved(Entity<ScreenSaturationComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Owner == _players.LocalEntity)
            RemoveOverlay();
    }

    private void OnPlayerAttached(Entity<ScreenSaturationComponent> ent, ref PlayerAttachedEvent args)
    {
        if (_enabled)
            AddOverlay();
    }

    private void OnPlayerDetached(Entity<ScreenSaturationComponent> ent, ref PlayerDetachedEvent args)
    {
        RemoveOverlay();
    }

    private void AddOverlay()
    {
        if (!_overlays.HasOverlay<ScreenSaturationOverlay>())
            _overlays.AddOverlay(_overlay);
    }

    private void RemoveOverlay()
    {
        if (_overlays.HasOverlay<ScreenSaturationOverlay>())
            _overlays.RemoveOverlay(_overlay);
    }
}
