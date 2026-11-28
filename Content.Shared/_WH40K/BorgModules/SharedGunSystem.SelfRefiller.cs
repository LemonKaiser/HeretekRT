using Content.Shared.Emp;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    private void InitializeSelfRefiller()
    {
        SubscribeLocalEvent<BallisticAmmoSelfRefillerComponent, MapInitEvent>(OnBallisticRefillerMapInit);
        SubscribeLocalEvent<BallisticAmmoSelfRefillerComponent, GunShotEvent>(OnBallisticRefillerShot);
        SubscribeLocalEvent<BallisticAmmoSelfRefillerComponent, EmpPulseEvent>(OnRefillerEmpPulsed);
    }

    private void OnBallisticRefillerMapInit(Entity<BallisticAmmoSelfRefillerComponent> entity, ref MapInitEvent args)
    {
        entity.Comp.NextAutoRefill = Timing.CurTime + entity.Comp.AutoRefillRate;
        DirtyField(entity.Owner, entity.Comp, nameof(BallisticAmmoSelfRefillerComponent.NextAutoRefill));
    }

    private void OnBallisticRefillerShot(Entity<BallisticAmmoSelfRefillerComponent> entity, ref GunShotEvent args)
    {
        PauseSelfRefill(entity);
    }

    private void OnRefillerEmpPulsed(Entity<BallisticAmmoSelfRefillerComponent> entity, ref EmpPulseEvent args)
    {
        if (!entity.Comp.AffectedByEmp)
            return;

        args.Affected = true;
        PauseSelfRefill(entity, args.Duration);
    }

    /// <summary>
    /// Delays the next refill by <paramref name="overridePauseDuration"/>, or by the component's pause duration if
    /// firing pauses it. Does nothing if the next refill is already later.
    /// </summary>
    public void PauseSelfRefill(Entity<BallisticAmmoSelfRefillerComponent> entity, TimeSpan? overridePauseDuration = null)
    {
        if (overridePauseDuration == null && !entity.Comp.FiringPausesAutoRefill)
            return;

        var nextRefillByPause = Timing.CurTime + (overridePauseDuration ?? entity.Comp.AutoRefillPauseDuration);
        if (nextRefillByPause <= entity.Comp.NextAutoRefill)
            return;

        entity.Comp.NextAutoRefill = nextRefillByPause;
        DirtyField(entity.Owner, entity.Comp, nameof(BallisticAmmoSelfRefillerComponent.NextAutoRefill));
    }

    /// <summary>
    /// Adds one round to the provider once the refill timer elapses and capacity is not yet reached.
    /// </summary>
    public void BallisticSelfRefillerUpdate(Entity<BallisticAmmoProviderComponent, BallisticAmmoSelfRefillerComponent> entity)
    {
        var ammo = entity.Comp1;
        var refiller = entity.Comp2;
        if (Timing.CurTime < refiller.NextAutoRefill)
            return;

        refiller.NextAutoRefill += refiller.AutoRefillRate;
        if (refiller.NextAutoRefill < Timing.CurTime)
            refiller.NextAutoRefill = Timing.CurTime + refiller.AutoRefillRate;
        DirtyField(entity.Owner, refiller, nameof(BallisticAmmoSelfRefillerComponent.NextAutoRefill));

        if (!refiller.AutoRefill || GetBallisticShots(ammo) >= ammo.Capacity)
            return;

        var proto = refiller.AmmoProto ?? ammo.Proto;
        if (proto == null)
        {
            Log.Error($"Neither {ToPrettyString(entity)}'s {nameof(BallisticAmmoSelfRefillerComponent)} nor its {nameof(BallisticAmmoProviderComponent)} names an ammo prototype.");
            return;
        }

        if (proto == ammo.Proto)
        {
            SetBallisticUnspawned((entity.Owner, ammo), ammo.UnspawnedCount + 1);
        }
        else
        {
            if (_netManager.IsClient)
                return;

            var ammoEntity = SpawnAttachedTo(proto, Transform(entity).Coordinates);
            ammo.Entities.Add(ammoEntity);
            Containers.Insert(ammoEntity, ammo.Container);
            UpdateBallisticAppearance(entity.Owner, ammo);
            UpdateAmmoCount(entity.Owner);
            DirtyField(entity.Owner, ammo, nameof(BallisticAmmoProviderComponent.Entities));
        }

        Audio.PlayPredicted(refiller.RechargeSound, entity.Owner, entity.Owner);
    }
}
