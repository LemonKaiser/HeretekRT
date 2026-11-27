using Content.Server.Physics.Controllers;
using Content.Shared._WH40K.Augments.Combat;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Augments.Combat;

public sealed class Wh40kReflexBoosterSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kReflexBoosterComponent, GetShuttleInputsEvent>(OnGetShuttleInputs);
        SubscribeLocalEvent<Wh40kReflexBoosterComponent, ProjectileReflectAttemptEvent>(OnProjectile);
        SubscribeLocalEvent<Wh40kReflexBoosterComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnGetShuttleInputs(Entity<Wh40kReflexBoosterComponent> ent, ref GetShuttleInputsEvent args)
    {
        args.AccelMul *= ent.Comp.ShipHandling;
        args.AngularMul *= ent.Comp.ShipHandling;
    }

    private void OnProjectile(Entity<Wh40kReflexBoosterComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled || !_mobState.IsAlive(ent) || !_random.Prob(ent.Comp.DodgeChance))
            return;

        args.Cancelled = true;
        _popup.PopupEntity(Loc.GetString("wh40k-reflex-booster-dodge", ("target", ent.Owner)), ent);
    }

    private void OnDamageModify(Entity<Wh40kReflexBoosterComponent> ent, ref DamageModifyEvent args)
    {
        if (ent.Comp.DamageVulnerabilityMultiplier <= 1f || args.Damage.Empty)
            return;

        var modified = new DamageSpecifier();
        foreach (var (type, amount) in args.Damage.DamageDict)
        {
            modified.DamageDict[type] = amount > 0
                ? amount * ent.Comp.DamageVulnerabilityMultiplier
                : amount;
        }

        args.Damage = modified;
    }
}
