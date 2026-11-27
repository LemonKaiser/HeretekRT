using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Goobstation.DoAfter;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Movement.Systems;

namespace Content.Shared._WH40K.Synthetics;

/// <summary>
/// Shared movement-speed and DoAfter-speed modifiers for Cybernetic (Mechanicus) and IPC chassis:
/// overclock (+25%), open bay (-20%), low power (flat 25% folding out silicon's own threshold slowdown),
/// cold seizure slowdown, and hand-reattached loose couplings (legs ×0.7 speed, arms ×1.5 DoAfter).
/// </summary>
public sealed class Wh40kChassisSpeedSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kChassisComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<Wh40kChassisComponent, GetDoAfterDelayMultiplierEvent>(OnDoAfterMultiplier);
    }

    private void OnRefreshSpeed(Entity<Wh40kChassisComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Overclocked)
            args.ModifySpeed(ent.Comp.OverclockSpeed, ent.Comp.OverclockSpeed);

        if (ent.Comp.BayOpen)
            args.ModifySpeed(ent.Comp.BayOpenSpeed, ent.Comp.BayOpenSpeed);

        if (ent.Comp.ColdSlowdown > 0.001f)
        {
            var coldMult = Math.Clamp(1f - ent.Comp.ColdSlowdown, 0.35f, 1f);
            args.ModifySpeed(coldMult, coldMult);
        }

        if (TryComp<Wh40kLowPowerComponent>(ent, out var lowPower))
        {
            var ownSlowdown = OwnSiliconSlowdown(ent);
            var target = Math.Clamp(lowPower.Speed / ownSlowdown, 0.05f, 4f);
            args.ModifySpeed(target, target);
        }

        var hasLooseLeg = false;
        var looseMoveMod = 1f;
        foreach (var (partUid, part) in _body.GetBodyChildren(ent))
        {
            if (part.PartType is not (BodyPartType.Leg or BodyPartType.Foot))
                continue;

            if (TryComp<Wh40kLooseCouplingComponent>(partUid, out var loose))
            {
                hasLooseLeg = true;
                looseMoveMod = MathF.Min(looseMoveMod, loose.MoveSpeedModifier);
            }
        }

        if (hasLooseLeg)
            args.ModifySpeed(looseMoveMod, looseMoveMod);
    }

    private void OnDoAfterMultiplier(EntityUid uid, Wh40kChassisComponent comp, GetDoAfterDelayMultiplierEvent args)
    {
        var looseArmMult = 1f;
        foreach (var (partUid, part) in _body.GetBodyChildren(uid))
        {
            if (part.PartType is not (BodyPartType.Arm or BodyPartType.Hand))
                continue;

            if (TryComp<Wh40kLooseCouplingComponent>(partUid, out var loose))
                looseArmMult = MathF.Max(looseArmMult, loose.DoAfterMultiplier);
        }

        if (looseArmMult > 1f)
            args.Multiplier *= looseArmMult;
    }

    private float OwnSiliconSlowdown(EntityUid body)
    {
        if (!TryComp<SiliconComponent>(body, out var silicon) || !silicon.BatteryPowered)
            return 1f;

        var closest = 1;
        foreach (var key in silicon.SpeedModifierThresholds.Keys)
        {
            if (silicon.ChargeState >= key && key > closest)
                closest = key;
        }

        if (silicon.SpeedModifierThresholds.TryGetValue(closest, out var modifier) && modifier > 0.01f)
            return modifier;

        return 1f;
    }
}
