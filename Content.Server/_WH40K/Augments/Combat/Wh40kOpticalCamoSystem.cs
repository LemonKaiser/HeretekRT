using Content.Shared._WH40K.Augments.Combat;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Emp;
using Content.Shared.Popups;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Augments.Combat;

public sealed class Wh40kOpticalCamoSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kOpticalCamoComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Wh40kOpticalCamoComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Wh40kOpticalCamoComponent, Wh40kToggleOpticalCamoEvent>(OnToggle);
        SubscribeLocalEvent<Wh40kOpticalCamoComponent, MeleeAttackEvent>(OnMeleeAttack);
        SubscribeLocalEvent<Wh40kOpticalCamoComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<Wh40kOpticalCamoComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<Wh40kOpticalCamoComponent, EmpPulseEvent>(OnEmp);
        SubscribeLocalEvent<Wh40kOpticalCamoComponent, Wh40kOpticalCamoRevealEvent>(OnRevealEvent);
    }

    private void OnStartup(Entity<Wh40kOpticalCamoComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<Wh40kOpticalCamoComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.EndsAt != null && !TerminatingOrDeleted(ent))
            EntityManager.RemoveComponents(ent, ent.Comp.Components);

        ent.Comp.EndsAt = null;
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        QueueDel(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnToggle(Entity<Wh40kOpticalCamoComponent> ent, ref Wh40kToggleOpticalCamoEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.EndsAt != null)
        {
            args.Handled = true;
            Reveal((ent, ent.Comp));
            return;
        }

        if (HasComp<StealthComponent>(ent))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-optical-camo-blocked"), ent, ent);
            return;
        }

        args.Handled = true;
        EntityManager.AddComponents(ent, ent.Comp.Components, removeExisting: false);
        if (TryComp<StealthComponent>(ent, out var stealth))
            _stealth.SetVisibility(ent, stealth.MinVisibility, stealth);

        ent.Comp.EndsAt = _timing.CurTime + ent.Comp.Duration;
        _actions.SetToggled(ent.Comp.ActionEntity, true);
        _audio.PlayPvs(ent.Comp.OnSound, ent);
    }

    private void OnMeleeAttack(Entity<Wh40kOpticalCamoComponent> ent, ref MeleeAttackEvent args)
        => Reveal((ent, ent.Comp));

    private void OnShotAttempted(Entity<Wh40kOpticalCamoComponent> ent, ref ShotAttemptedEvent args)
    {
        var gun = args.Used.Comp;
        if (args.Cancelled ||
            gun.NextFire > _timing.CurTime ||
            (gun.SelectedMode == SelectiveFire.SemiAuto && gun.ShotCounter > 0))
        {
            return;
        }

        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(args.Used, ref ammo);
        if (ammo.Capacity > 0 && ammo.Count <= 0)
            return;

        Reveal((ent, ent.Comp));
    }

    private void OnDamaged(Entity<Wh40kOpticalCamoComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased && args.Origin is { } origin && origin != ent.Owner)
            Reveal((ent, ent.Comp));
    }

    private void OnEmp(Entity<Wh40kOpticalCamoComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        Reveal((ent, ent.Comp), ent.Comp.EmpCooldown);
    }

    private void OnRevealEvent(Entity<Wh40kOpticalCamoComponent> ent, ref Wh40kOpticalCamoRevealEvent args)
        => Reveal((ent, ent.Comp));

    public void Reveal(Entity<Wh40kOpticalCamoComponent?> ent, TimeSpan? cooldown = null)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        var wasOn = ent.Comp.EndsAt != null;
        if (wasOn)
        {
            EntityManager.RemoveComponents(ent, ent.Comp.Components);
            ent.Comp.EndsAt = null;
            _actions.SetToggled(ent.Comp.ActionEntity, false);
            _audio.PlayPvs(ent.Comp.OffSound, ent);
            _popup.PopupEntity(Loc.GetString("wh40k-optical-camo-off"), ent, ent);
        }

        if (wasOn || cooldown != null)
            _actions.SetCooldown(ent.Comp.ActionEntity, cooldown ?? ent.Comp.Cooldown);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<Wh40kOpticalCamoComponent>();
        while (query.MoveNext(out var uid, out var camo))
        {
            if (camo.EndsAt is { } end && now >= end)
                Reveal((uid, camo));
        }
    }
}
