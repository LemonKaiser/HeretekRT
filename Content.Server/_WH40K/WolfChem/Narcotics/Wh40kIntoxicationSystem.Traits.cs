using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Stunnable;
using Content.Shared.Traits.Assorted;

namespace Content.Server._WH40K.WolfChem.Narcotics;

public sealed partial class Wh40kIntoxicationSystem
{
    [Dependency] private DamageableSystem _damageable = default!;

    /// <summary>Share of incoming Brute and Burn a <see cref="Wh40kDrugTrait.Damper"/> tier holds off.</summary>
    public const float DamperReduction = 0.3f;

    /// <summary>Share of held-off damage repaid as Poison.</summary>
    public const float DamperRepay = 0.4f;

    /// <summary>Share of a stun or knockdown a <see cref="Wh40kDrugTrait.StunResistance"/> tier leaves.</summary>
    public const float StunResistance = 0.5f;

    /// <summary>Cardiac strain each combat drug past the first deals per second on an organic body.</summary>
    public const float CombatStackStrain = 0.1f;

    private const float MinRepaySeconds = 5f;
    private const float MinRepayRate = 0.5f;

    private static readonly string[] DamperGroups = { "Brute", "Burn" };
    private static readonly Wh40kDrugTrait[] ComponentTraits =
    {
        Wh40kDrugTrait.IgnoreDamageSlowdown, Wh40kDrugTrait.StaminaCritResistance, Wh40kDrugTrait.HideDamage,
    };

    private readonly HashSet<Wh40kDrugTrait> _activeTraits = new();

    public override void Initialize()
    {
        base.Initialize();
        InitializeBlood();
        SubscribeLocalEvent<Wh40kIntoxicationComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<Wh40kIntoxicationComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Wh40kIntoxicationComponent, StunnedEvent>((EntityUid uid, Wh40kIntoxicationComponent _, ref StunnedEvent _) => ResistStun(uid, "Stun"));
        SubscribeLocalEvent<Wh40kIntoxicationComponent, KnockedDownEvent>((EntityUid uid, Wh40kIntoxicationComponent _, ref KnockedDownEvent _) => ResistStun(uid, "KnockedDown"));
    }

    private void UpdateTraits(Entity<Wh40kIntoxicationComponent> ent, float seconds, bool alive)
    {
        var (uid, comp) = ent;
        _activeTraits.Clear();
        var combat = 0;
        foreach (var (profile, level) in comp.Levels)
        {
            if (!Proto.TryIndex(profile, out var proto))
                continue;

            var tier = proto.TierAt(level);
            if (tier < 0)
                continue;

            _activeTraits.UnionWith(proto.Tiers[tier].Traits);
            if (proto.Family == Wh40kDrugFamily.Combat)
                combat++;
        }

        if (combat > 1 && alive && !IsNeurowareHost(uid))
        {
            var strain = CombatStackStrain * (combat - 1) * seconds;
            var damage = new DamageSpecifier();
            damage.DamageDict["Asphyxiation"] = FixedPoint2.New(strain * 6f);
            damage.DamageDict["Poison"] = FixedPoint2.New(strain * 2f);
            _damageable.TryChangeDamage(uid, damage, ignoreResistances: true, interruptsDoAfters: false, canSever: false);
        }

        foreach (var trait in ComponentTraits)
        {
            if (_activeTraits.Contains(trait))
                Grant(ent, trait);
            else if (comp.Granted.Remove(trait))
                RemoveTraitComponent(uid, trait);
        }

        if (comp.Debt > 0f && !_activeTraits.Contains(Wh40kDrugTrait.Damper))
            Repay(ent, seconds);
    }

    private void ResistStun(EntityUid uid, string key)
    {
        if (!HasTrait(uid, Wh40kDrugTrait.StunResistance) || !_status.TryGetTime(uid, key, out var time))
            return;

        var left = time.Value.Item2 - _timing.CurTime;
        if (left > TimeSpan.Zero)
            _status.TryRemoveTime(uid, key, left * (1f - StunResistance));
    }

    private void Grant(Entity<Wh40kIntoxicationComponent> ent, Wh40kDrugTrait trait)
    {
        var uid = ent.Owner;
        var added = trait switch
        {
            Wh40kDrugTrait.IgnoreDamageSlowdown => !HasComp<IgnoreSlowOnDamageComponent>(uid) && AddComp<IgnoreSlowOnDamageComponent>(uid) != null,
            Wh40kDrugTrait.StaminaCritResistance => !HasComp<StamcritResistComponent>(uid) && AddComp<StamcritResistComponent>(uid) != null,
            Wh40kDrugTrait.HideDamage => !HasComp<PainNumbnessComponent>(uid) && AddComp<PainNumbnessComponent>(uid) != null,
            _ => false,
        };

        if (added)
            ent.Comp.Granted.Add(trait);
    }

    private void RemoveTraitComponent(EntityUid uid, Wh40kDrugTrait trait)
    {
        switch (trait)
        {
            case Wh40kDrugTrait.IgnoreDamageSlowdown:
                RemComp<IgnoreSlowOnDamageComponent>(uid);
                break;
            case Wh40kDrugTrait.StaminaCritResistance:
                RemComp<StamcritResistComponent>(uid);
                break;
            case Wh40kDrugTrait.HideDamage:
                RemComp<PainNumbnessComponent>(uid);
                break;
        }
    }

    private void OnShutdown(Entity<Wh40kIntoxicationComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        foreach (var trait in ent.Comp.Granted)
        {
            RemoveTraitComponent(ent, trait);
        }

        ent.Comp.Granted.Clear();
        ent.Comp.Levels.Clear();
        ent.Comp.Crashes.Clear();
        ent.Comp.NodEnd = default;
        Movement.RefreshMovementSpeedModifiers(ent);
    }

    private void OnDamageModify(Entity<Wh40kIntoxicationComponent> ent, ref DamageModifyEvent args)
    {
        if (!HasTrait(ent, Wh40kDrugTrait.Damper))
            return;

        var damage = new DamageSpecifier(args.Damage);
        var avoided = FixedPoint2.Zero;
        foreach (var group in DamperGroups)
        {
            if (!Proto.TryIndex<DamageGroupPrototype>(group, out var proto))
                continue;

            foreach (var type in proto.DamageTypes)
            {
                if (!damage.DamageDict.TryGetValue(type, out var value) || value <= 0)
                    continue;

                var cut = value * DamperReduction;
                damage.DamageDict[type] = value - cut;
                avoided += cut;
            }
        }

        if (avoided <= 0)
            return;

        args.Damage = damage;
        ent.Comp.Debt += avoided.Float() * DamperRepay;
    }

    private void Repay(Entity<Wh40kIntoxicationComponent> ent, float seconds)
    {
        var left = MinRepaySeconds;
        foreach (var crash in ent.Comp.Crashes.Values)
        {
            left = Math.Max(left, crash);
        }

        var amount = Math.Min(ent.Comp.Debt, Math.Max(ent.Comp.Debt * seconds / left, MinRepayRate * seconds));
        ent.Comp.Debt -= amount;

        var poison = new DamageSpecifier();
        poison.DamageDict["Poison"] = FixedPoint2.New(amount);
        _damageable.TryChangeDamage(ent, poison, ignoreResistances: true, interruptsDoAfters: false, canSever: false);
    }
}
