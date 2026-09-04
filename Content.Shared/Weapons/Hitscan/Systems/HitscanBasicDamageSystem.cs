using Content.Shared.Damage;
using Content.Shared._WH40K.ItemRarity.Components;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Shared.Weapons.Hitscan.Systems;

public sealed partial class HitscanBasicDamageSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HitscanBasicDamageComponent, HitscanRaycastFiredEvent>(OnHitscanHit, after: [ typeof(HitscanReflectSystem) ]);
    }

    private void OnHitscanHit(Entity<HitscanBasicDamageComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled) // Mono
            return;

        var dmg = ent.Comp.Damage * _damage.UniversalHitscanDamageModifier;

        var weaponDamageMultiplier = 1f;
        var weaponArmorPenetration = 0f;
        if (TryComp<ItemRarityStatsComponent>(args.Gun, out var rarityStats) && rarityStats.Applied)
        {
            weaponDamageMultiplier = rarityStats.EffectiveWeaponDamageMultiplier;
            weaponArmorPenetration = rarityStats.EffectiveWeaponArmorPenetration;
        }

        dmg *= weaponDamageMultiplier;

        var sourceCoordinates = _transform.ToMapCoordinates(args.FromCoordinates);
        var impactCoordinates = new MapCoordinates(
            sourceCoordinates.Position + args.ShotDirection * args.DistanceTried,
            sourceCoordinates.MapId);

        foreach (var hitEntity in args.HitEntities) // Mono edit
        {
            var damageDealt = _damage.TryChangeDamage(hitEntity,
                dmg,
                origin: args.Gun,
                tool: args.Gun,
                armorPenetration: ent.Comp.ArmorPenetration + weaponArmorPenetration,
                ignoreResistances: ent.Comp.IgnoreResistances,
                impactCoordinates: impactCoordinates); // Mono - AP

            if (damageDealt == null)
                return;

            var damageEvent = new HitscanDamageDealtEvent
            {
                Target = hitEntity, // Mono
                DamageDealt = damageDealt,
            };

            RaiseLocalEvent(ent, ref damageEvent);
        }
    }
}
