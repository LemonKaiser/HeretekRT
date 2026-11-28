using Content.Shared._WH40K.Augments.Combat;
using Content.Shared._White.Overlays;
using Robust.Client.Graphics;
using Robust.Client.Player;

namespace Content.Client._WH40K.Augments.Combat;

public sealed partial class Wh40kThermalTintsVisualsSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;

    private Wh40kThermalGlitchOverlay _glitch = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kThermalTintsComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<Wh40kThermalTintsComponent, Wh40kThermalTintSwappedEvent>(OnSwapped);

        _glitch = new Wh40kThermalGlitchOverlay();
        _overlays.AddOverlay(_glitch);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_glitch);
    }

    private void OnState(Entity<Wh40kThermalTintsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (TryComp<ThermalVisionComponent>(ent, out var thermal))
            Wh40kThermalTintsSystem.ApplyTint(ent.Comp, thermal);
    }

    private void OnSwapped(Entity<Wh40kThermalTintsComponent> ent, ref Wh40kThermalTintSwappedEvent args)
    {
        if (ent.Owner == _player.LocalEntity)
            _glitch.Start(args.Color);
    }
}
