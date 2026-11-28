using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Shared._WH40K.ArmorDeflection;

/// <summary>
/// Ricochets light projectiles off <see cref="Wh40kArmorDeflectComponent"/> armor.
/// </summary>
public sealed partial class Wh40kArmorDeflectSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kArmorDeflectComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<Wh40kArmorDeflectComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<Wh40kArmorDeflectComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<Wh40kArmorDeflectWearerComponent, ProjectileReflectAttemptEvent>(OnReflectAttempt,
            after: [typeof(ReflectSystem)]);
    }

    private void OnEquipped(Entity<Wh40kArmorDeflectComponent> ent, ref GotEquippedEvent args)
    {
        EnsureComp<Wh40kArmorDeflectWearerComponent>(args.Equipee);
    }

    private void OnUnequipped(Entity<Wh40kArmorDeflectComponent> ent, ref GotUnequippedEvent args)
    {
        if (!TryGetDeflector(args.Equipee, out _, except: ent.Owner))
            RemComp<Wh40kArmorDeflectWearerComponent>(args.Equipee);
    }

    private void OnExamined(Entity<Wh40kArmorDeflectComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wh40k-armor-deflect-examine", ("threshold", ent.Comp.Threshold.Int())));
    }

    private bool TryGetDeflector(EntityUid wearer, out Wh40kArmorDeflectComponent deflector, EntityUid? except = null,
        ProjectileComponent? round = null)
    {
        var slots = _inventory.GetSlotEnumerator(wearer);
        while (slots.NextItem(out var item))
        {
            if (item == except || !TryComp(item, out Wh40kArmorDeflectComponent? found) ||
                round != null && !IsLightRound(found, round))
                continue;

            deflector = found;
            return true;
        }

        deflector = default!;
        return false;
    }

    private void OnReflectAttempt(Entity<Wh40kArmorDeflectWearerComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled || !TryGetDeflector(ent.Owner, out var armor, round: args.Component) ||
            !TryComp<PhysicsComponent>(args.ProjUid, out var physics))
            return;

        var rotation = _random.NextAngle(-armor.Spread / 2, armor.Spread / 2).Opposite();
        var existing = _physics.GetMapLinearVelocity(args.ProjUid, component: physics);
        var relative = existing - _physics.GetMapLinearVelocity(ent.Owner);
        var bounced = rotation.RotateVec(relative);
        _physics.SetLinearVelocity(args.ProjUid, physics.LinearVelocity + (bounced - existing), body: physics);
        var rot = Transform(args.ProjUid).LocalRotation;
        _transform.SetLocalRotation(args.ProjUid, rotation.RotateVec(rot.ToVec()).ToAngle());

        args.Cancelled = true;

        if (!_net.IsServer)
            return;

        _audio.PlayPvs(armor.Sound, ent.Owner);
        _popup.PopupEntity(Loc.GetString("wh40k-armor-deflect-popup", ("target", ent.Owner)), ent.Owner);
    }

    public static bool IsLightRound(Wh40kArmorDeflectComponent armor, ProjectileComponent projectile)
    {
        if (projectile.IgnoreResistances || projectile.ArmorPenetration >= armor.PenetratingArmorPenetration ||
            !projectile.Damage.DamageDict.TryGetValue(armor.DamageType, out var raw) || raw <= 0)
            return false;

        var effective = raw * (1f + MathF.Min(projectile.ArmorPenetration, 0f));
        return effective <= armor.Threshold;
    }
}
