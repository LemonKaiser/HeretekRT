using Content.Shared._White.Overlays;
using Content.Shared.Actions;
using Content.Shared.NightVision;
using Content.Shared.Overlays;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Cycles <see cref="Wh40kThermalTintsComponent"/> optics through thermal and night-vision bands.
/// </summary>
public sealed partial class Wh40kThermalTintsSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedNightVisionSystem _nightVision = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly Color OffColor = Color.FromHex("#9aa3a8");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kThermalTintsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Wh40kThermalTintsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Wh40kThermalTintsComponent, Wh40kCycleThermalTintEvent>(OnCycle);
    }

    private void OnStartup(Entity<Wh40kThermalTintsComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<Wh40kThermalTintsComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
        {
            if (TryComp<ThermalVisionComponent>(ent, out var thermal) && thermal.IsActive)
            {
                thermal.IsActive = false;
                Dirty(ent, thermal);
                var toggled = new SwitchableOverlayToggledEvent(ent, false);
                RaiseLocalEvent(ent, ref toggled);
            }

            if (TryComp<NightVisionComponent>(ent, out var night) && night.Enabled)
                _nightVision.SetEnabled((ent, night), false);
        }

        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnCycle(Entity<Wh40kThermalTintsComponent> ent, ref Wh40kCycleThermalTintEvent args)
    {
        if (args.Handled || !TryComp<ThermalVisionComponent>(ent, out var thermal))
            return;

        args.Handled = true;
        var next = ent.Comp.Index + 1 < ent.Comp.Tints.Count ? ent.Comp.Index + 1 : -1;
        SetTint(ent, next, thermal);

        if (!_timing.IsFirstTimePredicted)
            return;

        if (next < 0)
        {
            _audio.PlayLocal(ent.Comp.OffSound, ent, ent);
        }
        else
        {
            _audio.PlayLocal(ent.Comp.SwapSound, ent, ent);
            _audio.PlayLocal(ent.Comp.ChirpSound, ent, ent);
        }

        var swapped = new Wh40kThermalTintSwappedEvent(next < 0 ? OffColor : ent.Comp.Tints[next].Color);
        RaiseLocalEvent(ent, ref swapped);
    }

    public void SetTint(Entity<Wh40kThermalTintsComponent> ent, int index, ThermalVisionComponent thermal)
    {
        ent.Comp.Index = index;
        Dirty(ent);
        ApplyTint(ent.Comp, thermal);

        var band = index >= 0 && index < ent.Comp.Tints.Count ? ent.Comp.Tints[index] : null;
        if (TryComp<NightVisionComponent>(ent, out var night))
        {
            if (band is { NightVision: true } && night.PhosphorColor != band.Color)
            {
                night.PhosphorColor = band.Color;
                Dirty(ent, night);
            }

            _nightVision.SetEnabled((ent, night), band is { NightVision: true });
        }

        var active = band is { NightVision: false };
        if (thermal.IsActive == active)
        {
            Dirty(ent, thermal);
            return;
        }

        thermal.IsActive = active;
        Dirty(ent, thermal);
        var toggled = new SwitchableOverlayToggledEvent(ent, active);
        RaiseLocalEvent(ent, ref toggled);
    }

    public static void ApplyTint(Wh40kThermalTintsComponent comp, ThermalVisionComponent thermal)
    {
        if (comp.Index < 0 || comp.Index >= comp.Tints.Count || comp.Tints[comp.Index].NightVision)
            return;

        var tint = comp.Tints[comp.Index];
        thermal.Color = tint.Color;
        thermal.Tint = tint.Tint;
    }
}
