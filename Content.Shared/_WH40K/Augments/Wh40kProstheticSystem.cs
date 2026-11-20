using Content.Shared._Shitmed.Body.Events;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Systems;

namespace Content.Shared._WH40K.Augments;

public sealed partial class Wh40kProstheticSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    private readonly HashSet<EntityUid> _refresh = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<Wh40kProstheticComponent, DamageModifyEvent>(OnDamage, after: [typeof(SharedBodySystem)]);
        SubscribeLocalEvent<Wh40kProstheticComponent, BodyPartComponentsModifyEvent>(OnPartChanged);
    }

    private void OnDamage(Entity<Wh40kProstheticComponent> ent, ref DamageModifyEvent args)
    {
        var factor = 1f - ent.Comp.BonusPercent / 100f;
        var damage = new DamageSpecifier();
        foreach (var (type, amount) in args.Damage.DamageDict)
            damage.DamageDict[type] = amount > 0 ? amount * factor : amount;

        args.Damage = damage;
    }

    private void OnPartChanged(Entity<Wh40kProstheticComponent> ent, ref BodyPartComponentsModifyEvent args)
    {
        _refresh.Add(args.Body);
    }

    public override void Update(float frameTime)
    {
        foreach (var uid in _refresh)
        {
            if (!TerminatingOrDeleted(uid))
                _movement.RefreshMovementSpeedModifiers(uid);
        }

        _refresh.Clear();
    }

    /// <summary>
    /// Recovers a fraction of the speed lost to a specific penalty. Use the strongest fitted part for that penalty.
    /// </summary>
    public (float Walk, float Sprint) ReduceSlowdown(EntityUid uid, BodyPartType type, float walk, float sprint)
    {
        if (walk >= 1f && sprint >= 1f)
            return (walk, sprint);

        var percent = 0;
        foreach (var (part, component) in _body.GetBodyChildrenOfType(uid, type))
        {
            if (component.Enabled && TryComp<Wh40kProstheticComponent>(part, out var prosthetic))
                percent = Math.Max(percent, prosthetic.BonusPercent);
        }

        return (Reduce(walk, percent), Reduce(sprint, percent));
    }

    private static float Reduce(float multiplier, int percent)
    {
        return multiplier is > 0f and < 1f ? multiplier + (1f - multiplier) * percent / 100f : multiplier;
    }
}
