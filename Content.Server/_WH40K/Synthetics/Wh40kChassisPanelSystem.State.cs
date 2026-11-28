using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.PowerCell;
using Content.Server.Temperature.Components;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WH40K.Augments;
using Content.Shared._WH40K.Synthetics;
using Content.Shared._WH40K.WolfChem.Neuroware;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power.Components;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Synthetics;

public sealed partial class Wh40kChassisPanelSystem
{
    public const string BatterySlot = "battery";

    private static readonly (string Slot, TargetBodyPart Target)[] PartSlots =
    {
        ("head", TargetBodyPart.Head),
        ("torso", TargetBodyPart.Torso),
        ("left arm", TargetBodyPart.LeftArm),
        ("right arm", TargetBodyPart.RightArm),
        ("left hand", TargetBodyPart.LeftHand),
        ("right hand", TargetBodyPart.RightHand),
        ("left leg", TargetBodyPart.LeftLeg),
        ("right leg", TargetBodyPart.RightLeg),
        ("left foot", TargetBodyPart.LeftFoot),
        ("right foot", TargetBodyPart.RightFoot),
    };

    private static readonly HashSet<string> VitalSlots = new() { "brain", "posbrain", "pump", "heart" };

    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly Wh40kRadCollectorSystem _radCollector = default!;

    public Wh40kChassisPanelState BuildState(EntityUid body)
    {
        var state = new Wh40kChassisPanelState();
        var chassis = CompOrNull<Wh40kChassisComponent>(body);
        var hud = CompOrNull<Wh40kCyberneticHudComponent>(body);

        var attached = new Dictionary<TargetBodyPart, Entity<BodyPartComponent>>();
        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (GetTargetBodyPart(bodyPart) is { } target)
                attached.TryAdd(target, (part, bodyPart));
        }

        var slots = TryComp<BodyComponent>(body, out var bodyComp) &&
                    bodyComp.Prototype != null &&
                    _prototypes.TryIndex(bodyComp.Prototype.Value, out var prototype)
            ? prototype.Slots.Keys.ToHashSet()
            : null;

        foreach (var (slot, target) in PartSlots)
        {
            if (slots != null ? !slots.Contains(slot) : !attached.ContainsKey(target))
                continue;

            state.Parts.Add(BuildPartEntry(
                target,
                attached.TryGetValue(target, out var present) ? present : null,
                hud));
        }

        var docked = _augmentBed.TryGetDockedTable(body, out _, out var tablePowered);
        var tableReady = docked && tablePowered;

        foreach (var (target, part) in attached.OrderBy(pair => Array.FindIndex(PartSlots, s => s.Target == pair.Key)))
        {
            foreach (var slot in part.Comp.Organs.Keys)
                state.Organs.Add(BuildOrganEntry(part, target, slot, chassis, tableReady));
        }

        if (state.Organs.All(organ => organ.Slot != BatterySlot))
            state.Organs.Add(BuildSlotCellEntry(body, chassis, tableReady));

        var (augmentOptions, installedAugments) = _augmentBed.ScanForChassisPanel(body, includeInventoryOptions: docked);
        state.AugmentOptions = augmentOptions;
        state.InstalledAugments = installedAugments;

        var dead = _mobState.IsDead(body);
        var critical = _mobState.IsCritical(body);
        var lowPower = HasComp<Wh40kLowPowerComponent>(body);
        var rebooting = HasComp<Wh40kChassisRebootComponent>(body);

        state.BayOpen = chassis?.BayOpen ?? false;
        state.OnAugmentTable = docked;
        state.AugmentTablePowered = tableReady;
        state.AugmentFocusSeq = chassis?.AugmentFocusSeq ?? 0;
        state.Overclocked = chassis?.Overclocked ?? false;
        state.LowPower = lowPower;
        state.Rebooting = rebooting;
        state.Dead = dead;
        state.Power = TryGetCell(body, out var cell) ? ChargeFraction(cell.Value.Comp) : -1f;
        state.Integrity = ComputeBodyIntegrity(body);
        state.Sensors = ComputeSensorHeadroom(body);
        state.CoreTemperature = TryComp<TemperatureComponent>(body, out var temp) ? temp.CurrentTemperature : -1f;
        state.ChassisHeat = chassis != null ? Math.Clamp(chassis.ChassisHeat / OverclockHeatLimit, 0f, 1f) : 0f;

        if (_radCollector.GetCollector(body) is { } collector && collector.Comp.Capacity > 0f)
        {
            state.RadCollector = Math.Clamp(collector.Comp.Charge / collector.Comp.Capacity, 0f, 1f);
            state.RadCollectorIntegrity = collector.Comp.MaxIntegrity > 0f
                ? Math.Clamp(collector.Comp.Integrity / collector.Comp.MaxIntegrity, 0f, 1f)
                : 1f;
        }

        state.Radiation = _radCollector.GetExposure(body);
        state.Status = dead ? "wh40k-chassis-status-dead"
            : critical ? "wh40k-chassis-status-critical"
            : rebooting ? "wh40k-chassis-status-reboot"
            : lowPower ? "wh40k-chassis-status-low-power"
            : state.BayOpen ? "wh40k-chassis-status-bay-open"
            : state.Overclocked ? "wh40k-chassis-status-overclocked"
            : "wh40k-chassis-status-nominal";

        return state;
    }

    public void SyncHud(EntityUid body)
    {
        if (!TryComp<Wh40kCyberneticHudComponent>(body, out var hud))
            return;

        var chassis = CompOrNull<Wh40kChassisComponent>(body);
        var integrity = ComputeBodyIntegrity(body);
        var power = TryGetCell(body, out var cell) ? ChargeFraction(cell.Value.Comp) : -1f;
        var sensors = ComputeSensorHeadroom(body);
        var temp = TryComp<TemperatureComponent>(body, out var tempComp) ? tempComp.CurrentTemperature : -1f;
        var heat = chassis != null ? Math.Clamp(chassis.ChassisHeat / OverclockHeatLimit, 0f, 1f) : 0f;
        var collector = _radCollector.GetCollector(body);
        var radFrac = collector is { } found && found.Comp.Capacity > 0f
            ? Math.Clamp(found.Comp.Charge / found.Comp.Capacity, 0f, 1f)
            : -1f;
        var exposure = _radCollector.GetExposure(body);
        var lowPower = HasComp<Wh40kLowPowerComponent>(body);
        var rebooting = HasComp<Wh40kChassisRebootComponent>(body);
        var overclocked = chassis?.Overclocked ?? false;
        var bayOpen = chassis?.BayOpen ?? false;
        var critical = _mobState.IsCritical(body);
        var dead = _mobState.IsDead(body);

        var attached = new Dictionary<TargetBodyPart, Entity<BodyPartComponent>>();
        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (GetTargetBodyPart(bodyPart) is { } target)
                attached.TryAdd(target, (part, bodyPart));
        }

        var faults = new List<Wh40kCyberneticFault>();
        var expectedLimbs = 0;
        var workingLimbs = 0;

        foreach (var (_, target) in PartSlots)
        {
            if (target is TargetBodyPart.Head or TargetBodyPart.Torso)
                continue;

            expectedLimbs++;
            if (!attached.TryGetValue(target, out var part))
            {
                faults.Add(new Wh40kCyberneticFault(target, "wh40k-hud-fault-detached", Wh40kCyberneticSeverity.Fail, "wh40k-hud-advice-detached"));
                continue;
            }

            if (_isolation.IsUserIsolated(part))
            {
                faults.Add(new Wh40kCyberneticFault(target, "wh40k-hud-fault-isolated", Wh40kCyberneticSeverity.Info, "wh40k-hud-advice-isolated"));
                continue;
            }

            if (HasComp<Wh40kLowPowerPartComponent>(part))
            {
                faults.Add(new Wh40kCyberneticFault(target, "wh40k-hud-fault-unpowered", Wh40kCyberneticSeverity.Warn, "wh40k-hud-advice-unpowered"));
                continue;
            }

            if (!part.Comp.Enabled)
            {
                faults.Add(new Wh40kCyberneticFault(target, "wh40k-hud-fault-offline", Wh40kCyberneticSeverity.Fail, "wh40k-hud-advice-offline"));
                continue;
            }

            workingLimbs++;
            if (HasComp<Wh40kLooseCouplingComponent>(part))
            {
                faults.Add(new Wh40kCyberneticFault(target, "wh40k-hud-fault-loose", Wh40kCyberneticSeverity.Warn, "wh40k-hud-advice-loose"));
            }
        }

        var servos = expectedLimbs > 0 ? Math.Clamp((float) workingLimbs / expectedLimbs, 0f, 1f) : 1f;

        if (lowPower)
            faults.Add(new Wh40kCyberneticFault(TargetBodyPart.Torso, "wh40k-hud-fault-low-power", Wh40kCyberneticSeverity.Crit, "wh40k-hud-advice-low-power"));
        else if (power >= 0f && power < 0.20f)
            faults.Add(new Wh40kCyberneticFault(TargetBodyPart.Torso, "wh40k-hud-fault-low-charge", Wh40kCyberneticSeverity.Warn, "wh40k-hud-advice-low-charge"));

        if (heat >= 0.55f)
            faults.Add(new Wh40kCyberneticFault(TargetBodyPart.Torso, "wh40k-hud-fault-overheat", heat >= 0.80f ? Wh40kCyberneticSeverity.Crit : Wh40kCyberneticSeverity.Warn, "wh40k-hud-advice-overheat"));

        if (temp >= 0f && temp < Wh40kChassisColdSystem.StiffTemperature)
            faults.Add(new Wh40kCyberneticFault(TargetBodyPart.Torso, "wh40k-hud-fault-cold", temp <= Wh40kChassisColdSystem.SeizeTemperature ? Wh40kCyberneticSeverity.Crit : Wh40kCyberneticSeverity.Warn, "wh40k-hud-advice-cold"));

        if (collector == null)
            faults.Add(new Wh40kCyberneticFault(TargetBodyPart.Torso, "wh40k-hud-fault-rad-missing", exposure > 0f ? Wh40kCyberneticSeverity.Crit : Wh40kCyberneticSeverity.Warn, "wh40k-hud-advice-rad-missing"));
        else if (radFrac >= 0.99f)
            faults.Add(new Wh40kCyberneticFault(TargetBodyPart.Torso, "wh40k-hud-fault-rad-saturated", Wh40kCyberneticSeverity.Crit, "wh40k-hud-advice-rad-saturated"));

        if (bayOpen)
            faults.Add(new Wh40kCyberneticFault(TargetBodyPart.Torso, "wh40k-hud-fault-bay-open", Wh40kCyberneticSeverity.Info, "wh40k-hud-advice-bay-open"));

        if (TryComp<DamageableComponent>(body, out var damageable))
        {
            var brute = damageable.Damage.DamageDict.GetValueOrDefault("Blunt").Float() +
                        damageable.Damage.DamageDict.GetValueOrDefault("Slash").Float() +
                        damageable.Damage.DamageDict.GetValueOrDefault("Piercing").Float();
            var heatDmg = damageable.Damage.DamageDict.GetValueOrDefault("Heat").Float();
            var shockDmg = damageable.Damage.DamageDict.GetValueOrDefault("Shock").Float();

            if (brute >= 20f)
                faults.Add(new Wh40kCyberneticFault(TargetBodyPart.Torso, "wh40k-hud-fault-brute", brute >= 60f ? Wh40kCyberneticSeverity.Crit : Wh40kCyberneticSeverity.Warn, "wh40k-hud-advice-brute"));
            if (heatDmg >= 20f)
                faults.Add(new Wh40kCyberneticFault(TargetBodyPart.Torso, "wh40k-hud-fault-heat", heatDmg >= 60f ? Wh40kCyberneticSeverity.Crit : Wh40kCyberneticSeverity.Warn, "wh40k-hud-advice-heat"));
            if (shockDmg >= 15f)
                faults.Add(new Wh40kCyberneticFault(TargetBodyPart.Torso, "wh40k-hud-fault-shock", shockDmg >= 50f ? Wh40kCyberneticSeverity.Crit : Wh40kCyberneticSeverity.Warn, "wh40k-hud-advice-shock"));
        }

        var collectEv = new Wh40kCyberneticHudCollectEvent(faults);
        RaiseLocalEvent(body, ref collectEv);

        faults.Sort((a, b) => b.Severity.CompareTo(a.Severity));
        if (faults.Count > Wh40kCyberneticHudComponent.MaxFaults)
            faults.RemoveRange(Wh40kCyberneticHudComponent.MaxFaults, faults.Count - Wh40kCyberneticHudComponent.MaxFaults);

        var advice = faults.Count > 0 ? faults[0].Advice : string.Empty;
        var banner = lowPower ? "wh40k-hud-banner-low-power"
            : heat >= 0.75f ? "wh40k-hud-banner-overheat"
            : string.Empty;

        var changed =
            MathF.Abs(hud.Integrity - integrity) > 0.005f ||
            MathF.Abs(hud.Power - power) > 0.005f ||
            MathF.Abs(hud.Servos - servos) > 0.005f ||
            MathF.Abs(hud.Sensors - sensors) > 0.005f ||
            MathF.Abs(hud.CoreTemperature - temp) > 0.5f ||
            MathF.Abs(hud.ChassisHeat - heat) > 0.01f ||
            MathF.Abs(hud.RadCollector - radFrac) > 0.005f ||
            MathF.Abs(hud.Radiation - exposure) > 0.05f ||
            hud.LowPower != lowPower ||
            hud.Rebooting != rebooting ||
            hud.Overclocked != overclocked ||
            hud.BayOpen != bayOpen ||
            hud.Critical != critical ||
            hud.Dead != dead ||
            hud.Advice != advice ||
            hud.Banner != banner ||
            !hud.Faults.SequenceEqual(faults);

        if (!changed)
            return;

        hud.Integrity = integrity;
        hud.Power = power;
        hud.Servos = servos;
        hud.Sensors = sensors;
        hud.CoreTemperature = temp;
        hud.ChassisHeat = heat;
        hud.RadCollector = radFrac;
        hud.Radiation = exposure;
        hud.LowPower = lowPower;
        hud.Rebooting = rebooting;
        hud.Overclocked = overclocked;
        hud.BayOpen = bayOpen;
        hud.Critical = critical;
        hud.Dead = dead;
        hud.Advice = advice;
        hud.Banner = banner;
        hud.Faults = faults;
        Dirty(body, hud);
    }

    private Wh40kChassisPartEntry BuildPartEntry(
        TargetBodyPart target,
        Entity<BodyPartComponent>? part,
        Wh40kCyberneticHudComponent? hud)
    {
        var entry = new Wh40kChassisPartEntry
        {
            Slot = target,
            Name = Wh40kCyberneticHudHelper.PartLocKey(target),
        };

        if (hud != null)
        {
            foreach (var fault in hud.Faults)
            {
                if (fault.Part == target)
                    entry.Faults.Add(fault.Line);
            }
        }

        if (part is not { } attached)
            return entry;

        entry.Part = GetNetEntity(attached);
        entry.Attached = true;
        entry.Isolated = _isolation.IsUserIsolated(attached);
        if (TryComp<Wh40kLooseCouplingComponent>(attached, out var loose))
        {
            entry.Loose = true;
            entry.LoosePasses = loose.RemainingPasses;
        }

        entry.Working = attached.Comp.Enabled && !entry.Isolated && !HasComp<Wh40kLowPowerPartComponent>(attached);
        entry.Integrity = ComputePartIntegrity(attached);
        entry.CanDetach = Wh40kChassisIsolationSystem.CanIsolate(attached.Comp);
        return entry;
    }

    private Wh40kChassisOrganEntry BuildOrganEntry(
        Entity<BodyPartComponent> part,
        TargetBodyPart holder,
        string slot,
        Wh40kChassisComponent? chassis,
        bool tableReady)
    {
        var entry = new Wh40kChassisOrganEntry
        {
            Slot = slot,
            Holder = holder,
            Vital = VitalSlots.Contains(slot)
                && !(part.Comp.Body is { } body && Wh40kAugmentCatalog.IsAuxiliaryHeart(EntityManager, body, slot)),
        };

        if (!_containers.TryGetContainer(part, SharedBodySystem.GetOrganContainerId(slot), out var container) ||
            container.ContainedEntities.Count == 0)
        {
            return entry;
        }

        FillInstalledOrgan(entry, container.ContainedEntities[0], chassis, tableReady);
        return entry;
    }

    private Wh40kChassisOrganEntry BuildSlotCellEntry(EntityUid body, Wh40kChassisComponent? chassis, bool tableReady)
    {
        var entry = new Wh40kChassisOrganEntry { Slot = BatterySlot, Holder = TargetBodyPart.Torso, IsBattery = true };
        if (_powerCell.TryGetBatteryFromSlot(body, out var cell, out _))
            FillInstalledOrgan(entry, cell.Value, chassis, tableReady);

        return entry;
    }

    private void FillInstalledOrgan(Wh40kChassisOrganEntry entry, EntityUid installed, Wh40kChassisComponent? chassis, bool tableReady)
    {
        entry.Organ = GetNetEntity(installed);
        entry.Name = Name(installed);
        entry.IsBattery = HasComp<PowerCellComponent>(installed);
        entry.CanDisconnect = chassis?.BayOpen == true &&
            (entry.IsBattery || CompOrNull<OrganComponent>(installed)?.Removable == true) &&
            (tableReady || CanServiceInField(installed));

        if (TryComp<BatteryComponent>(installed, out var battery))
        {
            entry.Health = ChargeFraction(battery);
            entry.Band = "wh40k-chassis-band-charge";
        }
        else if (TryComp<Wh40kRadCollectorComponent>(installed, out var rad))
        {
            var frac = rad.MaxIntegrity > 0f ? Math.Clamp(rad.Integrity / rad.MaxIntegrity, 0f, 1f) : 1f;
            entry.Health = frac;
            entry.Band = frac < 0.25f ? "wh40k-chassis-band-failed"
                : frac < 0.7f ? "wh40k-chassis-band-impaired"
                : "wh40k-chassis-band-normal";
        }
        else
        {
            entry.Health = 1f;
            entry.Band = "wh40k-chassis-band-normal";
        }
    }

    public bool TryGetCell(EntityUid body, [NotNullWhen(true)] out Entity<BatteryComponent>? cell)
    {
        if (HasComp<SiliconComponent>(body) && TryComp<BatteryComponent>(body, out var own))
        {
            cell = (body, own);
            return true;
        }

        if (_powerCell.TryGetBatteryFromSlot(body, out var slotted, out var battery))
        {
            cell = (slotted.Value, battery);
            return true;
        }

        cell = null;
        return false;
    }

    private float ComputeBodyIntegrity(EntityUid body)
    {
        if (!TryComp<DamageableComponent>(body, out var damageable))
            return 1f;

        var max = 200f;
        if (_thresholds.TryGetDeadThreshold(body, out var deadThreshold) && deadThreshold.Value > 0)
            max = deadThreshold.Value.Float();

        return Math.Clamp(1f - damageable.TotalDamage.Float() / max, 0f, 1f);
    }

    private float ComputePartIntegrity(Entity<BodyPartComponent> part)
    {
        if (!TryComp<DamageableComponent>(part, out var dmg) || part.Comp.SeverIntegrity <= 0f)
            return 1f;

        return Math.Clamp(1f - dmg.TotalDamage.Float() / part.Comp.SeverIntegrity, 0f, 1f);
    }

    private float ComputeSensorHeadroom(EntityUid body)
    {
        var stamFrac = 1f;
        if (TryComp<StaminaComponent>(body, out var stam) && stam.CritThreshold > 0f)
            stamFrac = Math.Clamp(1f - stam.StaminaDamage / stam.CritThreshold, 0f, 1f);

        return MathF.Min(stamFrac, ComputeBodyIntegrity(body));
    }

    private static float ChargeFraction(BatteryComponent battery) =>
        battery.MaxCharge > 0f ? Math.Clamp(battery.CurrentCharge / battery.MaxCharge, 0f, 1f) : 0f;

    public static TargetBodyPart? GetTargetBodyPart(BodyPartComponent part) => (part.PartType, part.Symmetry) switch
    {
        (BodyPartType.Head, _) => TargetBodyPart.Head,
        (BodyPartType.Torso, _) => TargetBodyPart.Torso,
        (BodyPartType.Arm, BodyPartSymmetry.Left) => TargetBodyPart.LeftArm,
        (BodyPartType.Arm, BodyPartSymmetry.Right) => TargetBodyPart.RightArm,
        (BodyPartType.Hand, BodyPartSymmetry.Left) => TargetBodyPart.LeftHand,
        (BodyPartType.Hand, BodyPartSymmetry.Right) => TargetBodyPart.RightHand,
        (BodyPartType.Leg, BodyPartSymmetry.Left) => TargetBodyPart.LeftLeg,
        (BodyPartType.Leg, BodyPartSymmetry.Right) => TargetBodyPart.RightLeg,
        (BodyPartType.Foot, BodyPartSymmetry.Left) => TargetBodyPart.LeftFoot,
        (BodyPartType.Foot, BodyPartSymmetry.Right) => TargetBodyPart.RightFoot,
        _ => null,
    };
}
