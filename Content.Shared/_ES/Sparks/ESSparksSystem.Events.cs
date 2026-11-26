using Content.Shared._ES.Sparks.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Spawners;

namespace Content.Shared._ES.Sparks;

public sealed partial class ESSparksSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ESSparkOnHitComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<ESSparkOnItemToggleComponent, ItemToggledEvent>(OnItemToggled);
        SubscribeLocalEvent<ESSparkOnProjectileHitComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<ESSparkOnDespawnComponent, TimedDespawnEvent>(OnDespawn);
    }

    private void OnDamaged(Entity<ESSparkOnHitComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageDelta is null)
            return;

        if (args.DamageDelta.GetTotal() < ent.Comp.Threshold)
            return;

        DoSparks(ent);
    }

    private void OnItemToggled(Entity<ESSparkOnItemToggleComponent> ent, ref ItemToggledEvent args)
    {
        if (args.Activated != ent.Comp.ActivatedSpark)
            return;
        DoSparks(ent);
    }

    private void OnProjectileHit(Entity<ESSparkOnProjectileHitComponent> ent, ref ProjectileHitEvent args)
    {
        DoSparks(ent);
    }

    private void OnDespawn(Entity<ESSparkOnDespawnComponent> ent, ref TimedDespawnEvent args)
    {
        DoSparks(ent);
    }
}
