using Content.Shared._DV.CCVars;
using Content.Shared._WH40K.DroneVision;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._WH40K.DroneVision;

public sealed partial class WH40KDroneVisionSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;

    private WH40KDroneVisionOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KDroneVisionComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<WH40KDroneVisionComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<WH40KDroneVisionComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<WH40KDroneVisionComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        _overlay = new WH40KDroneVisionOverlay();
        Subs.CVar(_cfg, DCCVars.NoVisionFilters, _ => RefreshOverlay(), true);
    }

    private void OnComponentInit(EntityUid uid, WH40KDroneVisionComponent component, ComponentInit args)
    {
        if (uid == _playerManager.LocalEntity)
            RefreshOverlay();
    }

    private void OnComponentShutdown(EntityUid uid, WH40KDroneVisionComponent component, ComponentShutdown args)
    {
        if (uid == _playerManager.LocalEntity)
            _overlayManager.RemoveOverlay(_overlay);
    }

    private void OnPlayerAttached(EntityUid uid, WH40KDroneVisionComponent component, LocalPlayerAttachedEvent args)
    {
        RefreshOverlay();
    }

    private void OnPlayerDetached(EntityUid uid, WH40KDroneVisionComponent component, LocalPlayerDetachedEvent args)
    {
        _overlayManager.RemoveOverlay(_overlay);
    }

    private void RefreshOverlay()
    {
        var player = _playerManager.LocalEntity;
        var shouldShow = player is { Valid: true } &&
                         HasComp<WH40KDroneVisionComponent>(player.Value) &&
                         !_cfg.GetCVar(DCCVars.NoVisionFilters);

        if (shouldShow)
        {
            if (!_overlayManager.HasOverlay<WH40KDroneVisionOverlay>())
                _overlayManager.AddOverlay(_overlay);
        }
        else
        {
            _overlayManager.RemoveOverlay(_overlay);
        }
    }
}
