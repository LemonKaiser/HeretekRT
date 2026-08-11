using System.Linq;
using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Doors.Systems;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Server.Stealth;
using Content.Server.Weapons.Ranged.Systems;
using Content.Server._WH40K.Progression;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Shared.Actions;
using Content.Shared.ActionBlocker;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Doors;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Maps;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Stacks;
using Content.Shared.Standing;
using Content.Shared.Stealth.Components;
using Content.Shared.Tag;
using Content.Shared.Tools.Components;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Hitscan.Systems;
using Content.Shared._WH40K.ClassProgression;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared._White.BackStab;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Server.Player;
using Robust.Server.Audio;

namespace Content.Server._WH40K.ClassProgression;

/// <summary>
/// Executes body-local class mechanics. Every entry point resolves an immutable active effect and repeats
/// equipment, target, range, line-of-sight, cooldown, mob-state and Safeguard checks.
/// </summary>
public sealed class Wh40kClassGameplaySystem : EntitySystem
{
    private const float MaximumPassiveBonus = 0.5f;
    private const float MaximumDamageReduction = 0.75f;
    private static readonly TimeSpan CommandRefreshInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CommandLeaseDuration = TimeSpan.FromSeconds(1);
    private static readonly ProtoId<TagPrototype> HackableTag = "Wh40kClassHackable";
    private static readonly ProtoId<TagPrototype> MedicalNodeTag = "Wh40kMedicalNode";
    private static readonly ProtoId<DamageTypePrototype> PoisonDamageType = "Poison";
    private static readonly ProtoId<TagPrototype> PoisonConsumableTag = "Wh40kSkillPoison";
    private static readonly ProtoId<TagPrototype> SkillToolTag = "Wh40kSkillTool";
    private static readonly ProtoId<TagPrototype> CommandBeaconConsumableTag = "Wh40kCommandBeaconConsumable";
    private static readonly EntProtoId CommandBeaconPrototype = "Wh40kClassCommandBeacon";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private BackStabSystem _backstab = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private DoorSystem _doors = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private HandsSystem _serverHands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private MobThresholdSystem _mobThresholds = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private KoronusSafetyPolicySystem _safety = default!;
    [Dependency] private StealthSystem _stealth = default!;
    [Dependency] private Wh40kClassRuntimeSystem _runtime = default!;
    [Dependency] private Wh40kClassWeaponHandlingSystem _weaponHandling = default!;
    [Dependency] private Wh40kPartyManager _parties = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private TurfSystem _turf = default!;

    private TimeSpan _nextCommandRefresh;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, Wh40kClassInstantActionEvent>(OnInstantAction);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, Wh40kClassEntityTargetActionEvent>(OnEntityTargetAction);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, Wh40kClassWorldTargetActionEvent>(OnWorldTargetAction);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, BeforeStaminaDamageEvent>(OnBeforeStaminaDamage);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, SelfBeforeGunShotEvent>(OnBeforeGunShot);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, Wh40kClassProfileReconciledEvent>(OnProfileReconciled);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, ComponentRemove>(OnProfileRemoved);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, Wh40kClassDeviceDoAfterEvent>(OnDeviceDoAfter);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, Wh40kClassFinisherDoAfterEvent>(OnFinisherDoAfter);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, Wh40kClassVerdictDoAfterEvent>(OnVerdictDoAfter);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, Wh40kClassHealingAttemptEvent>(OnHealingAttempt);
        SubscribeLocalEvent<Wh40kClassRuntimeProfileComponent, BeforeThrowEvent>(OnBeforeThrow);
        SubscribeLocalEvent<Wh40kClassTransferConsentComponent, Wh40kClassInstantActionEvent>(OnTransferConsent);
        SubscribeLocalEvent<Wh40kClassTransferConsentComponent, ComponentShutdown>(OnTransferConsentRemoved);

        SubscribeLocalEvent<Wh40kClassEquipmentRelayComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<Wh40kClassEquipmentRelayComponent, GetMeleeAttackRateEvent>(OnGetMeleeAttackRate);
        SubscribeLocalEvent<Wh40kClassEquipmentRelayComponent, GetHeavyMeleeStaminaCostEvent>(OnGetHeavyMeleeStaminaCost);
        SubscribeLocalEvent<Wh40kClassEquipmentRelayComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<Wh40kClassEquipmentRelayComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<Wh40kClassThrownEffectComponent, ThrowDoHitEvent>(OnThrownEffectHit);
        SubscribeLocalEvent<Wh40kClassShotModifierComponent, ProjectileHitEvent>(OnShotModifierProjectileHit);
        SubscribeLocalEvent<Wh40kClassShotModifierComponent, HitscanRaycastFiredEvent>(OnShotModifierHitscanRaycast,
            before: [typeof(HitscanBasicDamageSystem)]);
        SubscribeLocalEvent<Wh40kClassShotModifierComponent, HitscanDamageDealtEvent>(OnShotModifierHitscanDamage);
        SubscribeLocalEvent<GunComponent, ItemSlotInsertAttemptEvent>(OnGunReloadSlotInsert);
        SubscribeLocalEvent<GunComponent, InteractUsingEvent>(OnGunReloadInteractUsing);

        SubscribeLocalEvent<Wh40kClassNpcPressureComponent, RefreshMovementSpeedModifiersEvent>(OnPressureMovement);
        SubscribeLocalEvent<Wh40kClassNpcPressureComponent, GunRefreshModifiersEvent>(OnPressureGun);
        SubscribeLocalEvent<Wh40kClassCommandRecipientComponent, BeforeDamageChangedEvent>(OnCommandBeforeDamage);
        SubscribeLocalEvent<Wh40kClassCommandRecipientComponent, BeforeStaminaDamageEvent>(OnCommandBeforeStaminaDamage);
        SubscribeLocalEvent<Wh40kClassCommandRecipientComponent, GunRefreshModifiersEvent>(OnCommandGun);
        SubscribeLocalEvent<Wh40kClassCommandRecipientComponent, RefreshMovementSpeedModifiersEvent>(OnCommandMovement);
        SubscribeLocalEvent<Wh40kClassInterceptTargetComponent, BeforeDamageChangedEvent>(OnInterceptedDamage);

        _parties.PartyChanged += OnPartyChanged;
    }

    public override void Shutdown()
    {
        _parties.PartyChanged -= OnPartyChanged;
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        if (now >= _nextCommandRefresh)
        {
            _nextCommandRefresh = now + CommandRefreshInterval;
            RefreshCommandRecipients();
        }

        var profiles = EntityQueryEnumerator<Wh40kClassRuntimeProfileComponent>();
        while (profiles.MoveNext(out var uid, out var profile))
        {
            foreach (var effectId in profile.RuntimeStates
                         .Where(pair => pair.Value.ExpiresAt <= now || pair.Value.Target is { } target && !Exists(target))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                RemoveRuntimeState((uid, profile), effectId);
            }

            foreach (var effectId in profile.CooldownEnds
                         .Where(pair => pair.Value <= now)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                profile.CooldownEnds.Remove(effectId);
            }
        }

        var pressures = EntityQueryEnumerator<Wh40kClassNpcPressureComponent>();
        while (pressures.MoveNext(out var uid, out var pressure))
        {
            var changed = false;
            foreach (var source in pressure.Sources
                         .Where(pair => pair.Value.ExpiresAt <= now || !Exists(pair.Key.Source))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                RemoveNpcPressureSource((uid, pressure), source);
                changed = true;
            }

            if (changed)
                _movement.RefreshMovementSpeedModifiers(uid);
            if (pressure.Sources.Count == 0)
                RemCompDeferred<Wh40kClassNpcPressureComponent>(uid);
        }

        var intercepts = EntityQueryEnumerator<Wh40kClassInterceptTargetComponent>();
        while (intercepts.MoveNext(out var uid, out var intercept))
        {
            foreach (var source in intercept.Sources
                         .Where(pair => pair.Value.ExpiresAt <= now || !Exists(pair.Key.Source))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                intercept.Sources.Remove(source);
            }

            if (intercept.Sources.Count == 0)
                RemCompDeferred<Wh40kClassInterceptTargetComponent>(uid);
        }

        var beacons = EntityQueryEnumerator<Wh40kClassCommandBeaconComponent>();
        while (beacons.MoveNext(out var uid, out var beacon))
        {
            if (beacon.ExpiresAt <= now || !Exists(beacon.Source) || !IsLiving(beacon.Source))
                QueueDel(uid);
        }

        var dashes = EntityQueryEnumerator<Wh40kClassDashRuntimeComponent, PhysicsComponent>();
        while (dashes.MoveNext(out var uid, out var dash, out var physics))
        {
            if (dash.EndsAt > now)
                continue;

            _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);
            RemCompDeferred<Wh40kClassDashRuntimeComponent>(uid);
        }

        var dashSpeeds = EntityQueryEnumerator<Wh40kClassDashSpeedRuntimeComponent>();
        while (dashSpeeds.MoveNext(out var uid, out var dashSpeed))
        {
            if (dashSpeed.EndsAt > now)
                continue;

            RemCompDeferred<Wh40kClassDashSpeedRuntimeComponent>(uid);
            _movement.RefreshMovementSpeedModifiers(uid);
        }

        var pendingShots = EntityQueryEnumerator<Wh40kClassPendingShotComponent>();
        while (pendingShots.MoveNext(out var uid, out var pendingShot))
        {
            if (pendingShot.ExpiresAt <= now)
                RemCompDeferred<Wh40kClassPendingShotComponent>(uid);
        }

        var nestRoutes = EntityQueryEnumerator<Wh40kClassNestRouteComponent>();
        while (nestRoutes.MoveNext(out var uid, out var route))
        {
            if (route.Points.Count > 0 && route.PointExpiry <= now)
            {
                route.Points.Clear();
                route.CycleCooldownEnd = now + TimeSpan.FromMinutes(5);
            }

            if (route.Points.Count == 0 && route.CycleCooldownEnd <= now)
                RemCompDeferred<Wh40kClassNestRouteComponent>(uid);
        }

        var transfers = EntityQueryEnumerator<Wh40kClassTransferConsentComponent>();
        while (transfers.MoveNext(out var uid, out var transfer))
        {
            if (transfer.ExpiresAt <= now || !Exists(transfer.Source) || !Exists(transfer.Item))
                RemCompDeferred<Wh40kClassTransferConsentComponent>(uid);
        }

        var overrides = EntityQueryEnumerator<Wh40kClassDeviceOverrideComponent>();
        while (overrides.MoveNext(out var uid, out var deviceOverride))
        {
            if (deviceOverride.ExpiresAt > now)
                continue;

            if (deviceOverride.ChangedBolts &&
                TryComp<DoorBoltComponent>(uid, out var bolts) &&
                bolts.BoltsDown != deviceOverride.OriginalBoltsDown)
            {
                _doors.SetBoltsDown((uid, bolts), deviceOverride.OriginalBoltsDown, deviceOverride.Source);
            }
            RemCompDeferred<Wh40kClassDeviceOverrideComponent>(uid);
        }

        var thrownEffects = EntityQueryEnumerator<Wh40kClassThrownEffectComponent>();
        while (thrownEffects.MoveNext(out var uid, out var thrownEffect))
        {
            if (thrownEffect.ExpiresAt <= now || !Exists(thrownEffect.Source))
                RemCompDeferred<Wh40kClassThrownEffectComponent>(uid);
        }

        var coatings = EntityQueryEnumerator<Wh40kClassWeaponCoatingComponent>();
        while (coatings.MoveNext(out var uid, out var coating))
        {
            if (coating.ExpiresAt <= now || !Exists(coating.Source) || coating.Charges <= 0)
                RemCompDeferred<Wh40kClassWeaponCoatingComponent>(uid);
        }
    }

    private void OnInstantAction(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref Wh40kClassInstantActionEvent args)
    {
        if (args.Handled || !TryResolveAction(ent, args.Action.Owner, null, out var effect))
            return;

        var success = effect.Mechanic switch
        {
            Wh40kClassRuntimeMechanic.GuardPreparation or
            Wh40kClassRuntimeMechanic.AttackPreparation or
            Wh40kClassRuntimeMechanic.TimedOffenseStance or
            Wh40kClassRuntimeMechanic.StationaryStance or
            Wh40kClassRuntimeMechanic.SuppressionMode or
            Wh40kClassRuntimeMechanic.MedicalProtocol => StartRuntimeState(ent, effect, null, Transform(ent).Coordinates),
            Wh40kClassRuntimeMechanic.TriageArea => StartTriageArea(ent, effect),
            Wh40kClassRuntimeMechanic.DeployHeldItem => DeployHeldMedicalNode(ent, effect),
            Wh40kClassRuntimeMechanic.WeaponCoating => ApplyWeaponCoating(ent, effect),
            Wh40kClassRuntimeMechanic.Cloak => StartCloak(ent, effect),
            Wh40kClassRuntimeMechanic.AreaPressure => ApplyAreaPressure(ent.Owner, Transform(ent).Coordinates, effect) > 0,
            Wh40kClassRuntimeMechanic.CommandAura => StartRuntimeState(ent, effect, null, Transform(ent).Coordinates),
            Wh40kClassRuntimeMechanic.CommandStamina => ApplyStaminaCommand(ent, effect),
            Wh40kClassRuntimeMechanic.CommandBeacon => DeployCommandBeacon(ent, effect),
            Wh40kClassRuntimeMechanic.FirePosition or
            Wh40kClassRuntimeMechanic.Barrage or
            Wh40kClassRuntimeMechanic.MeleeBreach or
            Wh40kClassRuntimeMechanic.HoldBreath => StartCostedRuntimeState(ent, effect),
            Wh40kClassRuntimeMechanic.VerdictShot => StartVerdictDoAfter(ent, args.Action.Owner, effect),
            Wh40kClassRuntimeMechanic.NestRoute when IsSecondaryAction(args.Action.Owner) => AdvanceNestRoute(ent, effect),
            _ => false,
        };

        if (!success)
            return;

        CommitAction(ent.Comp, effect);
        args.Handled = true;
    }

    private void OnEntityTargetAction(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref Wh40kClassEntityTargetActionEvent args)
    {
        if (args.Handled || !TryResolveAction(ent, args.Action.Owner, args.Target, out var effect))
            return;

        var success = effect.Mechanic switch
        {
            Wh40kClassRuntimeMechanic.TargetMark => StartRuntimeState(ent, effect, args.Target),
            Wh40kClassRuntimeMechanic.NpcPressure => ApplyTrackedNpcPressure(ent, args.Target, effect),
            Wh40kClassRuntimeMechanic.Intercept => StartIntercept(ent, args.Target, effect),
            Wh40kClassRuntimeMechanic.DashToEntity => StartDashToEntity(ent, args.Target, effect),
            Wh40kClassRuntimeMechanic.PullAlly => PullAlly(ent, args.Target, effect),
            Wh40kClassRuntimeMechanic.TransferHeldItem => RequestHeldItemTransfer(ent, args.Target, effect),
            Wh40kClassRuntimeMechanic.MedicalProtocol => StartRuntimeState(ent, effect, args.Target),
            Wh40kClassRuntimeMechanic.DeviceScan => ScanDevice(ent, args.Target, effect),
            Wh40kClassRuntimeMechanic.DeviceBypass or
            Wh40kClassRuntimeMechanic.DeviceDisable or
            Wh40kClassRuntimeMechanic.DeviceRepair => StartDeviceDoAfter(ent, args.Action.Owner, args.Target, effect),
            Wh40kClassRuntimeMechanic.PrivateInformation => StartRuntimeState(ent, effect, args.Target),
            Wh40kClassRuntimeMechanic.Finisher => StartFinisherDoAfter(ent, args.Action.Owner, args.Target, effect),
            _ => false,
        };

        if (!success)
            return;

        if (effect.Mechanic == Wh40kClassRuntimeMechanic.TargetMark)
            SendTargetMarkVisual(ent.Comp, args.Target, effect.Duration, false);
        CommitAction(ent.Comp, effect);
        args.Handled = true;
    }

    private void OnWorldTargetAction(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref Wh40kClassWorldTargetActionEvent args)
    {
        if (args.Handled || !TryResolveAction(ent, args.Action.Owner, null, out var effect) ||
            effect.Range <= 0f ||
            effect.Mechanic != Wh40kClassRuntimeMechanic.NestRoute &&
            !_interaction.InRangeUnobstructed(ent.Owner, args.Target, effect.Range))
        {
            return;
        }

        var success = effect.Mechanic switch
        {
            Wh40kClassRuntimeMechanic.Distraction => ApplyDistraction(ent, args.Target, effect),
            Wh40kClassRuntimeMechanic.AreaPressure => ApplyAreaPressure(ent.Owner, args.Target, effect) > 0,
            Wh40kClassRuntimeMechanic.DashToPoint => StartDash(ent, args.Target, effect),
            Wh40kClassRuntimeMechanic.CombatDash => StartCombatDash(ent, args.Target, effect, false),
            Wh40kClassRuntimeMechanic.AssaultJump => StartCombatDash(ent, args.Target, effect, true),
            Wh40kClassRuntimeMechanic.NestRoute when !IsSecondaryAction(args.Action.Owner) => PlaceNestPoint(ent, args.Target, effect),
            _ => false,
        };

        if (!success)
            return;

        CommitAction(ent.Comp, effect);
        if (effect.Mechanic is Wh40kClassRuntimeMechanic.CombatDash or Wh40kClassRuntimeMechanic.AssaultJump)
            ent.Comp.CooldownEnds[effect.EffectId] = _timing.CurTime + GetDashCooldown(ent.Comp, effect);
        args.Handled = true;
    }

    private bool ApplyDistraction(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityCoordinates target,
        Wh40kResolvedClassEffect effect)
    {
        if (effect.SupportingItem is not { Valid: true } item ||
            !TryComp<HandsComponent>(ent, out var hands) ||
            hands.ActiveHandEntity != item ||
            !_serverHands.ThrowHeldItem(ent.Owner, target))
        {
            return false;
        }

        ApplyAreaPressure(ent.Owner, target, effect);
        return StartRuntimeState(ent, effect, null, target);
    }

    private bool IsSecondaryAction(EntityUid action)
    {
        return TryComp<Wh40kClassGrantedActionComponent>(action, out var marker) && marker.IsSecondary;
    }

    private bool StartCostedRuntimeState(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        Wh40kResolvedClassEffect effect)
    {
        if (effect.StaminaCost > 0f && !_stamina.TryTakeStamina(ent.Owner, effect.StaminaCost))
            return false;

        return StartRuntimeState(ent, effect, null, Transform(ent).Coordinates);
    }

    private bool StartCombatDash(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityCoordinates target,
        Wh40kResolvedClassEffect effect,
        bool assaultJump)
    {
        var cost = GetDashCost(ent.Comp, effect);
        var range = GetDashRange(ent.Comp, effect);
        var duration = TimeSpan.FromSeconds(assaultJump ? 0.25 : 0.3);
        var configured = effect with { StaminaCost = cost, Range = range, Duration = duration };
        if (!StartDash(ent, target, configured, assaultJump))
            return false;

        var afterSpeed = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.DashAfterSpeed, null);
        if (afterSpeed > 0)
        {
            var dashSpeed = EnsureComp<Wh40kClassDashSpeedRuntimeComponent>(ent.Owner);
            dashSpeed.BonusPercent = Math.Clamp(afterSpeed, 1, 25);
            dashSpeed.EndsAt = _timing.CurTime + TimeSpan.FromSeconds(1);
            _movement.RefreshMovementSpeedModifiers(ent.Owner);
        }

        return true;
    }

    private float GetDashCost(Wh40kClassRuntimeProfileComponent profile, Wh40kResolvedClassEffect effect)
    {
        var baseCost = effect.StaminaCost > 0f ? effect.StaminaCost : 10f;
        var overrideCost = profile.ActiveEffects.Values
            .Where(active => active.Mechanic == Wh40kClassRuntimeMechanic.DashCostOverride)
            .Select(active => (float) active.Magnitude)
            .DefaultIfEmpty(baseCost)
            .Min();
        if (profile.LastMarksmanShotAt + TimeSpan.FromSeconds(3) > _timing.CurTime)
        {
            var reduction = profile.ActiveEffects.Values
                .Where(active => active.Mechanic == Wh40kClassRuntimeMechanic.DashShotCostReduction)
                .Sum(active => Math.Max(0, active.Magnitude));
            overrideCost -= reduction;
        }

        return Math.Max(0f, overrideCost);
    }

    private static float GetDashRange(Wh40kClassRuntimeProfileComponent profile, Wh40kResolvedClassEffect effect)
    {
        var baseRange = effect.Range > 0f ? effect.Range : 3f;
        return profile.ActiveEffects.Values
            .Where(active => active.Mechanic == Wh40kClassRuntimeMechanic.DashRangeOverride)
            .Select(active => active.Range > 0f ? active.Range : (float) active.Magnitude)
            .DefaultIfEmpty(baseRange)
            .Max();
    }

    private static TimeSpan GetDashCooldown(Wh40kClassRuntimeProfileComponent profile, Wh40kResolvedClassEffect effect)
    {
        var baseCooldown = effect.Cooldown > TimeSpan.Zero ? effect.Cooldown : TimeSpan.FromSeconds(5);
        return profile.ActiveEffects.Values
            .Where(active => active.Mechanic == Wh40kClassRuntimeMechanic.DashCooldownOverride && active.Cooldown > TimeSpan.Zero)
            .Select(active => active.Cooldown)
            .DefaultIfEmpty(baseCooldown)
            .Min();
    }

    private bool PlaceNestPoint(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityCoordinates target,
        Wh40kResolvedClassEffect effect)
    {
        var now = _timing.CurTime;
        var route = EnsureComp<Wh40kClassNestRouteComponent>(ent.Owner);
        if (route.CycleCooldownEnd > now || route.PlaceCooldownEnd > now || route.RouteLocked || route.Points.Count >= 4)
            return false;

        var candidate = _transform.ToMapCoordinates(target);
        if (!TryGetSafeDashTarget(candidate, out var safePoint))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-class-nest-invalid-point"), ent.Owner, ent.Owner, PopupType.Medium);
            return false;
        }

        var reference = route.Points.Count == 0
            ? _transform.GetMapCoordinates(ent.Owner)
            : route.Points[^1];
        var point = _transform.ToMapCoordinates(safePoint);
        if (reference.MapId != point.MapId || Vector2.DistanceSquared(reference.Position, point.Position) > effect.Range * effect.Range)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-class-nest-too-far"), ent.Owner, ent.Owner, PopupType.Medium);
            return false;
        }

        route.EffectId = effect.EffectId;
        route.Points.Add(point);
        route.RouteLocked = route.Points.Count == 4;
        route.PlaceCooldownEnd = now + TimeSpan.FromSeconds(2);
        route.PointExpiry = now + TimeSpan.FromMinutes(5);
        return true;
    }

    private bool AdvanceNestRoute(Entity<Wh40kClassRuntimeProfileComponent> ent, Wh40kResolvedClassEffect effect)
    {
        var now = _timing.CurTime;
        if (!TryComp<Wh40kClassNestRouteComponent>(ent, out var route) ||
            route.EffectId != effect.EffectId ||
            route.CycleCooldownEnd > now ||
            route.AdvanceCooldownEnd > now ||
            !route.RouteLocked ||
            route.Points.Count == 0)
        {
            return false;
        }

        var next = route.Points[0];
        var source = _transform.GetMapCoordinates(ent.Owner);
        if (source.MapId != next.MapId || Vector2.DistanceSquared(source.Position, next.Position) > effect.Range * effect.Range)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-class-nest-too-far"), ent.Owner, ent.Owner, PopupType.Medium);
            return false;
        }

        route.Points.RemoveAt(0);
        route.AdvanceCooldownEnd = now + TimeSpan.FromSeconds(1);
        if (route.Points.Count == 0)
            route.CycleCooldownEnd = now + TimeSpan.FromMinutes(5);

        StartRouteDash(ent, next);
        return true;
    }

    private void StartRouteDash(Entity<Wh40kClassRuntimeProfileComponent> ent, MapCoordinates target)
    {
        if (!IsLiving(ent.Owner) || !_actionBlocker.CanMove(ent.Owner) || _standing.IsDown(ent.Owner) ||
            !_safety.IsClassActionAllowed(ent.Owner, null, Wh40kClassEffectSafety.Mobility) ||
            !TryComp<PhysicsComponent>(ent, out var physics))
        {
            return;
        }

        var start = _transform.GetMapCoordinates(ent.Owner);
        if (start.MapId != target.MapId)
            return;

        var delta = target.Position - start.Position;
        var length = delta.Length();
        if (length < 0.05f)
            return;

        var lastSafe = start;
        var samples = Math.Max(1, (int) Math.Ceiling(length / 0.2f));
        for (var index = 1; index <= samples; index++)
        {
            var candidate = new MapCoordinates(start.Position + delta * (index / (float) samples), start.MapId);
            if (!TryGetSafeDashTarget(candidate, out var safe))
                break;
            lastSafe = _transform.ToMapCoordinates(safe);
        }

        var travel = lastSafe.Position - start.Position;
        if (travel.LengthSquared() < 0.01f)
            return;

        var duration = TimeSpan.FromSeconds(Math.Clamp(travel.Length() / 20f, 0.12f, 0.5f));
        _physics.SetLinearVelocity(ent.Owner, Vector2.Normalize(travel) * (travel.Length() / (float) duration.TotalSeconds), body: physics);
        EnsureComp<Wh40kClassDashRuntimeComponent>(ent).EndsAt = _timing.CurTime + duration;
    }

    private void OnBeforeThrow(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref BeforeThrowEvent args)
    {
        if (args.Cancelled || args.PlayerUid != ent.Owner)
            return;

        foreach (var (effectId, state) in ent.Comp.RuntimeStates.ToArray())
        {
            if (state.ExpiresAt <= _timing.CurTime ||
                !ent.Comp.ActiveEffects.TryGetValue(effectId, out var effect) ||
                effect.Mechanic != Wh40kClassRuntimeMechanic.AttackPreparation ||
                effect.SupportingItem != args.ItemUid)
            {
                continue;
            }

            var thrown = EnsureComp<Wh40kClassThrownEffectComponent>(args.ItemUid);
            thrown.Source = ent.Owner;
            thrown.EffectId = effect.EffectId;
            thrown.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(3, effect.Duration.TotalSeconds));
            thrown.Magnitude = Math.Clamp(effect.Magnitude, 5, 50);
            thrown.Duration = effect.Duration > TimeSpan.Zero ? effect.Duration : TimeSpan.FromSeconds(4);
            RemoveRuntimeState(ent, effectId);
            break;
        }

        BreakStates(ent, Wh40kClassCounterplay.BreakOnAttack);
    }

    private void OnThrownEffectHit(
        Entity<Wh40kClassThrownEffectComponent> ent,
        ref ThrowDoHitEvent args)
    {
        if (ent.Comp.ExpiresAt <= _timing.CurTime ||
            !Exists(ent.Comp.Source) ||
            !TryComp<Wh40kClassRuntimeProfileComponent>(ent.Comp.Source, out var profile) ||
            !profile.ActiveEffects.TryGetValue(ent.Comp.EffectId, out var effect) ||
            !_safety.IsClassActionAllowed(ent.Comp.Source, args.Target, effect.Safety))
        {
            RemCompDeferred<Wh40kClassThrownEffectComponent>(ent.Owner);
            return;
        }

        ApplyRuntimeSlow(ent.Comp.Source, args.Target, effect.EffectId, ent.Comp.Magnitude, ent.Comp.Duration);
        RemCompDeferred<Wh40kClassThrownEffectComponent>(ent.Owner);
    }

    private bool ApplyWeaponCoating(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        Wh40kResolvedClassEffect effect)
    {
        if (effect.SupportingItem is not { Valid: true } weapon || !_hands.IsHolding(ent.Owner, weapon, out _))
            return false;

        EntityUid? poison = null;
        Entity<SolutionComponent>? poisonSolution = null;
        var cost = FixedPoint2.New(5);
        foreach (var item in _runtime.BuildEquipmentSnapshot(ent.Owner).Select(snapshot => snapshot.Entity))
        {
            if (item == weapon || !_hands.IsHolding(ent.Owner, item, out _) || !_tags.HasTag(item, PoisonConsumableTag) ||
                !_solutions.TryGetSolution(item, "drink", out var solutionEntity, out var solution) ||
                solution.GetReagentQuantity(new ReagentId("Toxin", null)) < cost)
            {
                continue;
            }

            poison = item;
            poisonSolution = solutionEntity;
            break;
        }

        if (poison is not { } || poisonSolution is not { } solutionUid)
            return false;

        _solutions.RemoveReagent(solutionUid, "Toxin", cost);
        var coating = EnsureComp<Wh40kClassWeaponCoatingComponent>(weapon);
        coating.Source = ent.Owner;
        coating.EffectId = effect.EffectId;
        coating.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(10, effect.Duration.TotalSeconds));
        coating.Magnitude = Math.Clamp(effect.Magnitude, 1, 20);
        coating.Charges = 1;
        return StartRuntimeState(ent, effect, weapon);
    }

    private bool StartFinisherDoAfter(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityUid action,
        EntityUid target,
        Wh40kResolvedClassEffect effect)
    {
        if (!_backstab.TryBackstab(target, ent.Owner, Angle.FromDegrees(60), false, false, false))
            return false;

        var doAfter = new DoAfterArgs(
            EntityManager,
            ent.Owner,
            TimeSpan.FromSeconds(Math.Clamp(effect.Duration.TotalSeconds, 1, 12)),
            new Wh40kClassFinisherDoAfterEvent(),
            ent.Owner,
            target: target,
            used: action)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        };
        return _doAfter.TryStartDoAfter(doAfter);
    }

    private bool StartVerdictDoAfter(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityUid action,
        Wh40kResolvedClassEffect effect)
    {
        if (effect.StaminaCost > 0f && !_stamina.TryTakeStamina(ent.Owner, effect.StaminaCost))
            return false;

        var doAfter = new DoAfterArgs(
            EntityManager,
            ent.Owner,
            TimeSpan.FromSeconds(0.5),
            new Wh40kClassVerdictDoAfterEvent(),
            ent.Owner,
            used: action)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        };
        return _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnVerdictDoAfter(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref Wh40kClassVerdictDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } action ||
            !TryComp<Wh40kClassGrantedActionComponent>(action, out var marker) ||
            marker.Body != ent.Owner ||
            !ent.Comp.ActiveEffects.TryGetValue(marker.EffectId, out var effect) ||
            effect.Mechanic != Wh40kClassRuntimeMechanic.VerdictShot ||
            !IsLiving(ent.Owner) ||
            !HasLiveEquipment(ent.Owner, effect))
        {
            return;
        }

        args.Handled = true;
        if (!StartRuntimeState(ent, effect, null, Transform(ent).Coordinates))
            return;

        ent.Comp.RuntimeStates[effect.EffectId] = ent.Comp.RuntimeStates[effect.EffectId] with { Charges = 3 };
    }

    private void OnFinisherDoAfter(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref Wh40kClassFinisherDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target || args.Used is not { } action ||
            !TryComp<Wh40kClassGrantedActionComponent>(action, out var marker) ||
            marker.Body != ent.Owner ||
            !ent.Comp.ActiveEffects.TryGetValue(marker.EffectId, out var effect))
        {
            return;
        }

        args.Handled = true;
        if (!IsLiving(ent.Owner) ||
            !Exists(target) ||
            !HasLiveEquipment(ent.Owner, effect) ||
            !_interaction.InRangeUnobstructed(ent.Owner, target, effect.Range) ||
            !_safety.IsClassActionAllowed(ent.Owner, target, effect.Safety) ||
            effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresMarkedTarget) && !HasMark(ent.Comp, target) ||
            effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresDownedTarget) &&
            (!TryComp<MobStateComponent>(target, out var mob) || mob.CurrentState == MobState.Alive) ||
            !_backstab.TryBackstab(target, ent.Owner, Angle.FromDegrees(60), false, false, false))
        {
            return;
        }

        if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.PlayerNonLethal) && _safety.IsPlayerCharacter(target))
        {
            _stamina.TakeStaminaDamage(target, Math.Clamp(effect.Magnitude, 10, 60), source: ent.Owner);
        }
        else if (_prototypes.TryIndex<DamageTypePrototype>("Piercing", out var piercing))
        {
            _damageable.TryChangeDamage(
                target,
                new DamageSpecifier(piercing, Math.Clamp(effect.Magnitude, 5, 60)),
                origin: ent.Owner,
                interruptsDoAfters: true,
                canSever: false);
        }

        foreach (var effectId in ent.Comp.RuntimeStates
                     .Where(pair => pair.Value.Target == target &&
                                    ent.Comp.ActiveEffects.TryGetValue(pair.Key, out var active) &&
                                    active.Mechanic == Wh40kClassRuntimeMechanic.TargetMark)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            RemoveRuntimeState(ent, effectId);
        }
        BreakStates(ent, Wh40kClassCounterplay.BreakOnAttack);
    }

    private void ApplyRuntimeSlow(
        EntityUid source,
        EntityUid target,
        string effectId,
        int magnitude,
        TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        var pressure = EnsureComp<Wh40kClassNpcPressureComponent>(target);
        pressure.Sources[new Wh40kClassNpcPressureSource(source, effectId)] =
            new Wh40kClassNpcPressureState(
                _timing.CurTime + duration,
                Math.Clamp(Math.Abs(magnitude), 0, 60),
                false,
                Wh40kClassModifierCategory.Mobility);
        _movement.RefreshMovementSpeedModifiers(target);
    }

    private void OnHealingAttempt(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref Wh40kClassHealingAttemptEvent args)
    {
        var used = args.Used;
        if (args.Target == ent.Owner ||
            !Exists(args.Target) ||
            !_npcFaction.IsEntityFriendly(ent.Owner, args.Target) ||
            !_runtime.BuildEquipmentSnapshot(ent.Owner).Any(item => item.Entity == used))
        {
            return;
        }

        if (!args.Completing)
        {
            var speed = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.ServiceModifier, args.Used);
            args.DelayMultiplier *= 1f / (1f + Math.Clamp(speed / 100f, 0f, MaximumPassiveBonus));
            return;
        }

        var bonus = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.HealingModifier, args.Used);
        foreach (var (effectId, state) in ent.Comp.RuntimeStates.ToArray())
        {
            if (state.ExpiresAt <= _timing.CurTime ||
                state.Target != args.Target ||
                !ent.Comp.ActiveEffects.TryGetValue(effectId, out var effect) ||
                effect.Mechanic != Wh40kClassRuntimeMechanic.MedicalProtocol ||
                effect.SupportingItem != args.Used ||
                !_safety.IsClassActionAllowed(ent.Owner, args.Target, Wh40kClassEffectSafety.Supportive))
            {
                continue;
            }

            bonus += Math.Max(0, effect.Magnitude);
            RemoveRuntimeState(ent, effectId);
        }

        var multiplier = 1f + Math.Clamp(bonus / 100f, 0f, MaximumPassiveBonus);
        args.HealingMultiplier *= multiplier;
        args.BloodlossMultiplier *= multiplier;
    }

    private bool RequestHeldItemTransfer(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityUid target,
        Wh40kResolvedClassEffect effect)
    {
        if (effect.SupportingItem is not { Valid: true } item ||
            !_hands.IsHolding(ent.Owner, item, out _) ||
            !_hands.TryGetEmptyHand(target, out _))
        {
            return false;
        }

        if (TryComp<Wh40kClassTransferConsentComponent>(target, out _))
            RemComp<Wh40kClassTransferConsentComponent>(target);

        EntityUid? action = null;
        if (!_actions.AddAction(target, ref action, "ActionWh40kOperativeAcceptTransfer") || action is not { } actionUid)
            return false;

        var request = EnsureComp<Wh40kClassTransferConsentComponent>(target);
        request.Source = ent.Owner;
        request.Item = item;
        request.Action = actionUid;
        request.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Clamp(effect.Duration.TotalSeconds, 3, 15));
        return StartRuntimeState(ent, effect, target);
    }

    private void OnTransferConsent(
        Entity<Wh40kClassTransferConsentComponent> ent,
        ref Wh40kClassInstantActionEvent args)
    {
        if (args.Handled || args.Action.Owner != ent.Comp.Action)
            return;

        args.Handled = true;
        var success = ent.Comp.ExpiresAt > _timing.CurTime &&
                      IsLiving(ent.Owner) &&
                      IsLiving(ent.Comp.Source) &&
                      Exists(ent.Comp.Item) &&
                      _interaction.InRangeUnobstructed(ent.Comp.Source, ent.Owner, 2f) &&
                      _safety.IsClassActionAllowed(ent.Comp.Source, ent.Owner, Wh40kClassEffectSafety.Supportive) &&
                      _hands.IsHolding(ent.Comp.Source, ent.Comp.Item, out _) &&
                      _hands.TryGetEmptyHand(ent.Owner, out _);
        if (success && _hands.TryDrop(ent.Comp.Source, ent.Comp.Item))
        {
            if (!_hands.TryPickupAnyHand(ent.Owner, ent.Comp.Item))
                _hands.TryPickupAnyHand(ent.Comp.Source, ent.Comp.Item);
        }

        RemCompDeferred<Wh40kClassTransferConsentComponent>(ent.Owner);
    }

    private void OnTransferConsentRemoved(
        Entity<Wh40kClassTransferConsentComponent> ent,
        ref ComponentShutdown args)
    {
        if (Exists(ent.Comp.Action))
            _actions.RemoveAction(ent.Owner, ent.Comp.Action);
    }

    private bool DeployHeldMedicalNode(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        Wh40kResolvedClassEffect effect)
    {
        var node = _runtime.BuildEquipmentSnapshot(ent.Owner)
            .Select(item => item.Entity)
            .FirstOrDefault(item => _tags.HasTag(item, MedicalNodeTag) && _hands.IsHolding(ent.Owner, item, out _));
        if (!node.Valid || !_hands.TryDrop(ent.Owner, node, Transform(ent).Coordinates))
            return false;
        return StartRuntimeState(ent, effect, node, Transform(ent).Coordinates);
    }

    private bool StartTriageArea(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        Wh40kResolvedClassEffect effect)
    {
        if (!StartRuntimeState(ent, effect, null, Transform(ent).Coordinates))
            return false;

        var injured = EntityManager.System<EntityLookupSystem>()
            .GetEntitiesInRange<DamageableComponent>(Transform(ent).Coordinates, Math.Max(effect.Range, 1f))
            .Count(target => target.Owner != ent.Owner &&
                             target.Comp.TotalDamage > 0 &&
                             _npcFaction.IsEntityFriendly(ent.Owner, target.Owner) &&
                             _interaction.InRangeUnobstructed(ent.Owner, target.Owner, effect.Range));
        _popup.PopupEntity(
            Loc.GetString("wh40k-class-triage-scan-result", ("count", injured)),
            ent.Owner,
            ent.Owner,
            PopupType.Medium);
        return true;
    }

    private bool PullAlly(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityUid target,
        Wh40kResolvedClassEffect effect)
    {
        if (!TryComp<PhysicsComponent>(target, out var physics))
            return false;

        var source = _transform.GetMapCoordinates(ent.Owner);
        var targetCoordinates = _transform.GetMapCoordinates(target);
        if (source.MapId != targetCoordinates.MapId)
            return false;
        var delta = source.Position - targetCoordinates.Position;
        if (delta.LengthSquared() < 0.04f)
            return false;

        var duration = TimeSpan.FromSeconds(0.35);
        var speed = Math.Clamp(delta.Length() / (float) duration.TotalSeconds, 2f, 9f);
        _physics.SetLinearVelocity(target, Vector2.Normalize(delta) * speed, body: physics);
        EnsureComp<Wh40kClassDashRuntimeComponent>(target).EndsAt = _timing.CurTime + duration;
        return StartRuntimeState(ent, effect, target);
    }

    private bool ScanDevice(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityUid target,
        Wh40kResolvedClassEffect effect)
    {
        if (!IsHackableDevice(target) || !HasDeviceTool(ent.Owner, repairResource: false))
            return false;

        var damage = TryComp<DamageableComponent>(target, out var damageable)
            ? damageable.TotalDamage.Float()
            : 0f;
        var bolts = TryComp<DoorBoltComponent>(target, out var boltComponent) && boltComponent.BoltsDown;
        _popup.PopupEntity(
            Loc.GetString(
                "wh40k-class-device-scan-result",
                ("device", Name(target)),
                ("damage", MathF.Round(damage)),
                ("bolts", bolts)),
            target,
            ent.Owner,
            PopupType.Medium);
        return StartRuntimeState(ent, effect, target);
    }

    private bool StartDeviceDoAfter(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityUid action,
        EntityUid target,
        Wh40kResolvedClassEffect effect)
    {
        var repair = effect.Mechanic == Wh40kClassRuntimeMechanic.DeviceRepair;
        if (!IsHackableDevice(target) ||
            !HasDeviceTool(ent.Owner, repair) ||
            effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresOpenPanel) &&
            (!TryComp<Content.Shared.Wires.WiresPanelComponent>(target, out var panel) || !panel.Open) ||
            effect.Mechanic == Wh40kClassRuntimeMechanic.DeviceBypass &&
            !effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresOpenPanel) &&
            _doors.IsBolted(target))
        {
            LogDeviceAttempt(ent.Owner, target, effect, false);
            return false;
        }

        var service = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.ServiceModifier, effect.SupportingItem);
        var delay = effect.Duration.TotalSeconds / (1d + Math.Clamp(service / 100d, 0d, MaximumPassiveBonus));
        var doAfter = new DoAfterArgs(
            EntityManager,
            ent.Owner,
            TimeSpan.FromSeconds(Math.Clamp(delay, 1d, 12d)),
            new Wh40kClassDeviceDoAfterEvent(),
            ent.Owner,
            target: target,
            used: action)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        };
        return _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnDeviceDoAfter(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref Wh40kClassDeviceDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target || args.Used is not { } action ||
            !TryComp<Wh40kClassGrantedActionComponent>(action, out var marker) ||
            marker.Body != ent.Owner ||
            !ent.Comp.ActiveEffects.TryGetValue(marker.EffectId, out var effect))
        {
            return;
        }

        args.Handled = true;
        var repair = effect.Mechanic == Wh40kClassRuntimeMechanic.DeviceRepair;
        if (!IsLiving(ent.Owner) ||
            !IsHackableDevice(target) ||
            !HasLiveEquipment(ent.Owner, effect) ||
            !HasDeviceTool(ent.Owner, repair) ||
            !_interaction.InRangeUnobstructed(ent.Owner, target, effect.Range) ||
            !_safety.IsClassActionAllowed(ent.Owner, target, effect.Safety) ||
            effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresOpenPanel) &&
            (!TryComp<Content.Shared.Wires.WiresPanelComponent>(target, out var panel) || !panel.Open))
        {
            LogDeviceAttempt(ent.Owner, target, effect, false);
            return;
        }

        var success = effect.Mechanic switch
        {
            Wh40kClassRuntimeMechanic.DeviceBypass => CompleteDeviceBypass(ent.Owner, target, effect),
            Wh40kClassRuntimeMechanic.DeviceDisable => CompleteDeviceDisable(ent.Owner, target, effect),
            Wh40kClassRuntimeMechanic.DeviceRepair => CompleteDeviceRepair(ent.Owner, target, effect),
            _ => false,
        };
        LogDeviceAttempt(ent.Owner, target, effect, success);
        if (success)
            StartRuntimeState(ent, effect, target);
    }

    private bool CompleteDeviceBypass(EntityUid user, EntityUid target, Wh40kResolvedClassEffect effect)
    {
        if (!TryComp<DoorComponent>(target, out var door) || door.State == DoorState.Welded)
            return false;

        if (_doors.IsBolted(target))
        {
            if (!effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresOpenPanel) ||
                !TryComp<DoorBoltComponent>(target, out var bolts))
                return false;

            var runtime = EnsureComp<Wh40kClassDeviceOverrideComponent>(target);
            runtime.Source = user;
            runtime.EffectId = effect.EffectId;
            runtime.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(5, effect.Duration.TotalSeconds * 2));
            runtime.ChangedBolts = true;
            runtime.OriginalBoltsDown = true;
            _doors.SetBoltsDown((target, bolts), false, user);
        }

        return _doors.SetState(target, DoorState.Opening, door);
    }

    private bool CompleteDeviceDisable(EntityUid user, EntityUid target, Wh40kResolvedClassEffect effect)
    {
        if (!TryComp<DoorComponent>(target, out var door) || !TryComp<DoorBoltComponent>(target, out var bolts))
            return false;

        if (door.State == DoorState.Open)
            _doors.SetState(target, DoorState.Closing, door);
        var original = bolts.BoltsDown;
        if (!original)
            _doors.SetBoltsDown((target, bolts), true, user);

        var runtime = EnsureComp<Wh40kClassDeviceOverrideComponent>(target);
        runtime.Source = user;
        runtime.EffectId = effect.EffectId;
        runtime.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(5, effect.Duration.TotalSeconds * 2));
        runtime.ChangedBolts = !original;
        runtime.OriginalBoltsDown = original;
        return true;
    }

    private bool CompleteDeviceRepair(EntityUid user, EntityUid target, Wh40kResolvedClassEffect effect)
    {
        if (!TryComp<DamageableComponent>(target, out var damageable) ||
            damageable.TotalDamage <= 0 ||
            !_prototypes.TryIndex<DamageTypePrototype>("Structural", out var structural))
        {
            return false;
        }

        var resource = FindDeviceTool(user, repairResource: true);
        if (resource is not { } stackUid || !TryComp<StackComponent>(stackUid, out var stack) || stack.Count <= 0)
            return false;

        var repaired = _damageable.TryChangeDamage(
            target,
            new DamageSpecifier(structural, -Math.Clamp(effect.Magnitude, 1, 50)),
            origin: user,
            interruptsDoAfters: false,
            canSever: false);
        if (repaired == null || repaired.Empty)
            return false;

        _stacks.Use(stackUid, 1, stack);
        return true;
    }

    private bool IsHackableDevice(EntityUid target)
    {
        return Exists(target) && _tags.HasTag(target, HackableTag);
    }

    private bool HasDeviceTool(EntityUid user, bool repairResource)
    {
        return FindDeviceTool(user, repairResource) != null;
    }

    private EntityUid? FindDeviceTool(EntityUid user, bool repairResource)
    {
        foreach (var item in _runtime.BuildEquipmentSnapshot(user).Select(snapshot => snapshot.Entity))
        {
            if (!_hands.IsHolding(user, item, out _) || !_tags.HasTag(item, SkillToolTag))
                continue;
            if (!repairResource && HasComp<ToolComponent>(item))
                return item;
            if (repairResource && TryComp<StackComponent>(item, out var stack) && stack.Count > 0)
                return item;
        }

        return null;
    }

    private void LogDeviceAttempt(
        EntityUid user,
        EntityUid target,
        Wh40kResolvedClassEffect effect,
        bool success)
    {
        _adminLog.Add(
            LogType.Action,
            success ? LogImpact.Medium : LogImpact.Low,
            $"{ToPrettyString(user):user} {(success ? "completed" : "failed")} WH40K class device action " +
            $"{effect.EffectId} on {ToPrettyString(target):target}");
    }

    private void OnBeforeDamage(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled)
            return;

        ent.Comp.LastStationaryResetAt = _timing.CurTime;
        if (ent.Comp.RuntimeStates.Any(pair => pair.Value.ExpiresAt > _timing.CurTime &&
                                               ent.Comp.ActiveEffects.TryGetValue(pair.Key, out var active) &&
                                               active.Mechanic == Wh40kClassRuntimeMechanic.AssaultJump))
        {
            args.Cancelled = true;
            return;
        }

        var reduction = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.IncomingDamageReaction, null) / 100f;
        foreach (var (effectId, state) in ent.Comp.RuntimeStates)
        {
            if (!ent.Comp.ActiveEffects.TryGetValue(effectId, out var effect) || state.ExpiresAt <= _timing.CurTime)
                continue;
            if (effect.Mechanic is Wh40kClassRuntimeMechanic.GuardPreparation or
                Wh40kClassRuntimeMechanic.StationaryStance or Wh40kClassRuntimeMechanic.FirePosition)
                reduction += Math.Max(0, effect.Magnitude) / 100f;
            if (effect.Mechanic == Wh40kClassRuntimeMechanic.TimedOffenseStance)
                reduction -= Math.Max(0, effect.Magnitude) / 200f;
            if (effect.Mechanic == Wh40kClassRuntimeMechanic.NpcPressure && state.Target == args.Origin)
                reduction += Math.Max(0, effect.Magnitude) / 200f;
        }
        if (reduction > 0f)
            args.Damage *= 1f - MathF.Min(reduction, MaximumDamageReduction);
        BreakStates(ent, Wh40kClassCounterplay.BreakOnDamage);
    }

    private void OnBeforeStaminaDamage(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref BeforeStaminaDamageEvent args)
    {
        if (args.Cancelled)
            return;

        if (ent.Comp.RuntimeStates.Any(pair => pair.Value.ExpiresAt > _timing.CurTime &&
                                               ent.Comp.ActiveEffects.TryGetValue(pair.Key, out var active) &&
                                               active.Mechanic == Wh40kClassRuntimeMechanic.AssaultJump))
        {
            args.Cancelled = true;
            return;
        }

        var reduction = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.IncomingStaminaReaction, null) / 100f;
        reduction += ent.Comp.RuntimeStates
            .Where(pair => pair.Value.ExpiresAt > _timing.CurTime &&
                           ent.Comp.ActiveEffects.TryGetValue(pair.Key, out var effect) &&
                           effect.Mechanic is Wh40kClassRuntimeMechanic.GuardPreparation or
                               Wh40kClassRuntimeMechanic.StationaryStance or Wh40kClassRuntimeMechanic.FirePosition)
            .Sum(pair => Math.Max(0, ent.Comp.ActiveEffects[pair.Key].Magnitude)) / 100f;
        if (args.With is { Valid: true })
            reduction += GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.WeaponStaminaDamageReduction, null) / 100f;
        if (reduction > 0f)
            args.Value *= 1f - MathF.Min(reduction, MaximumDamageReduction);
    }

    private void OnGetMeleeDamage(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref GetMeleeDamageEvent args)
    {
        if (args.User != ent.Owner)
            return;

        var bonus = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.MeleeDamageModifier, args.Weapon,
            effect => !effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresInjuredTarget) &&
                      !effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresMarkedTarget) &&
                      !effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresBackstab)) / 100f;
        bonus += ent.Comp.RuntimeStates
            .Where(pair => pair.Value.ExpiresAt > _timing.CurTime &&
                           ent.Comp.ActiveEffects.TryGetValue(pair.Key, out var effect) &&
                           effect.Mechanic == Wh40kClassRuntimeMechanic.TimedOffenseStance)
            .Sum(pair => Math.Max(0, ent.Comp.ActiveEffects[pair.Key].Magnitude)) / 100f;
        if (bonus > 0f)
            args.Damage *= 1f + MathF.Min(bonus, MaximumPassiveBonus);
    }

    private void OnGetMeleeAttackRate(
        Entity<Wh40kClassEquipmentRelayComponent> ent,
        ref GetMeleeAttackRateEvent args)
    {
        if (args.User != ent.Comp.Body || !TryComp(ent.Comp.Body, out Wh40kClassRuntimeProfileComponent? profile))
            return;

        var bonus = GetCappedMagnitude(profile, Wh40kClassRuntimeMechanic.MeleeTempoModifier, ent.Owner) / 100f;
        if (bonus > 0f)
            args.Multipliers *= 1f + MathF.Min(bonus, MaximumPassiveBonus);
    }

    private void OnGetHeavyMeleeStaminaCost(
        Entity<Wh40kClassEquipmentRelayComponent> ent,
        ref GetHeavyMeleeStaminaCostEvent args)
    {
        if (args.User != ent.Comp.Body || !TryComp(ent.Comp.Body, out Wh40kClassRuntimeProfileComponent? profile))
            return;

        var reduction = Math.Clamp(
            GetCappedMagnitude(profile, Wh40kClassRuntimeMechanic.HeavyMeleeStaminaCostModifier, ent.Owner),
            0,
            75) / 100f;
        if (reduction > 0f)
            args.Cost *= 1f - reduction;
    }

    private void OnGunRefreshModifiers(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref GunRefreshModifiersEvent args)
    {
        var control = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.GunControlModifier, args.Gun.Owner) / 100f;
        control += ent.Comp.RuntimeStates
            .Where(pair => pair.Value.ExpiresAt > _timing.CurTime &&
                           ent.Comp.ActiveEffects.TryGetValue(pair.Key, out var effect) &&
                           effect.Mechanic is Wh40kClassRuntimeMechanic.StationaryStance or
                               Wh40kClassRuntimeMechanic.AttackPreparation)
            .Sum(pair => Math.Max(0, ent.Comp.ActiveEffects[pair.Key].Magnitude)) / 100f;
        if (control > 0f)
        {
            var multiplier = 1f - MathF.Min(control, MaximumPassiveBonus);
            args.CameraRecoilScalar *= multiplier;
            args.AngleIncrease *= multiplier;
            args.MinAngle *= multiplier;
            args.MaxAngle *= multiplier;
        }

        var tempo = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.GunTempoModifier, args.Gun.Owner) / 100f;
        if (tempo > 0f)
            args.FireRate *= 1f + MathF.Min(tempo, MaximumPassiveBonus);

        var angleIncrease = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.GunAngleIncreaseModifier, args.Gun.Owner) / 100f;
        var maxAngle = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.GunMaxAngleModifier, args.Gun.Owner) / 100f;
        if (angleIncrease > 0f)
            args.AngleIncrease *= 1f - MathF.Min(angleIncrease, MaximumPassiveBonus);
        if (maxAngle > 0f)
            args.MaxAngle *= 1f - MathF.Min(maxAngle, MaximumPassiveBonus);

        var angleDecay = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.GunAngleDecayModifier, args.Gun.Owner) / 100f;
        if (angleDecay > 0f)
            args.AngleDecay *= 1f + MathF.Min(angleDecay, MaximumPassiveBonus);

        var projectileSpeed = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.GunProjectileSpeedModifier, args.Gun.Owner) / 100f;
        if (projectileSpeed > 0f)
            args.ProjectileSpeed *= 1f + MathF.Min(projectileSpeed, MaximumPassiveBonus);

        var cameraRecoil = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.GunCameraRecoilModifier, args.Gun.Owner) / 100f;
        if (cameraRecoil > 0f)
            args.CameraRecoilScalar *= 1f - MathF.Min(cameraRecoil, MaximumPassiveBonus);

        if (args.Gun.Comp.SelectedMode == SelectiveFire.SemiAuto)
        {
            var semiTempo = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.SemiAutoGunTempoModifier, args.Gun.Owner) / 100f;
            if (semiTempo > 0f)
                args.FireRate *= 1f + MathF.Min(semiTempo, 0.10f);
        }

        foreach (var (effectId, state) in ent.Comp.RuntimeStates)
        {
            if (state.ExpiresAt <= _timing.CurTime || !ent.Comp.ActiveEffects.TryGetValue(effectId, out var active))
                continue;

            switch (active.Mechanic)
            {
                case Wh40kClassRuntimeMechanic.FirePosition:
                    args.AngleIncrease *= 1f - Math.Clamp(active.SecondaryMagnitude, 0, 50) / 100f;
                    args.MaxAngle *= 1f - Math.Clamp(active.TertiaryMagnitude, 0, 50) / 100f;
                    break;
                case Wh40kClassRuntimeMechanic.Barrage:
                    args.FireRate *= 1f + Math.Clamp(active.Magnitude, 0, 75) / 100f;
                    break;
                case Wh40kClassRuntimeMechanic.HoldBreath when args.Gun.Comp.SelectedMode == SelectiveFire.SemiAuto:
                    args.ProjectileSpeed *= 1f + Math.Clamp(active.Magnitude, 0, 50) / 100f;
                    args.FireRate *= 1f + Math.Clamp(active.SecondaryMagnitude, 0, 50) / 100f;
                    args.AngleIncrease *= 1f - Math.Clamp(active.TertiaryMagnitude, 0, 50) / 100f;
                    args.MaxAngle *= 1f - Math.Clamp(active.TertiaryMagnitude, 0, 50) / 100f;
                    break;
                case Wh40kClassRuntimeMechanic.VerdictShot when args.Gun.Comp.SelectedMode == SelectiveFire.SemiAuto:
                    args.ProjectileSpeed *= 1.10f;
                    args.AngleIncrease *= 0.5f;
                    args.MaxAngle *= 0.5f;
                    break;
            }
        }

        if (!_weaponHandling.IsMoving(ent.Owner) &&
            ent.Comp.LastStationaryResetAt + TimeSpan.FromSeconds(3) <= _timing.CurTime)
        {
            var stationaryControl = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.StationaryGunControlModifier, args.Gun.Owner) / 100f;
            if (stationaryControl > 0f)
            {
                var multiplier = 1f - MathF.Min(stationaryControl, MaximumPassiveBonus);
                args.AngleIncrease *= multiplier;
                args.MaxAngle *= multiplier;
            }
        }

        // GunRefreshModifiersEvent is raised on the holder before it is raised on the gun. Pre-scale the current
        // values so the category system's later moving-shot penalty lands on the compensated total.
        if (args.User is { Valid: true } user &&
            _weaponHandling.IsMoving(user) &&
            TryComp<Wh40kWeaponHandlingComponent>(args.Gun.Owner, out var handling))
        {
            var baseSpread = Wh40kClassWeaponHandlingSystem.GetValues(handling.Category).MovingSpreadPercent;
            var compensation = Math.Clamp(
                GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.GunMovingSpreadCompensation, args.Gun.Owner),
                0,
                75) / 100f;
            if (baseSpread > 0 && compensation > 0f)
            {
                var baselineMultiplier = 1f + baseSpread / 100f;
                var targetMultiplier = 1f + baseSpread / 100f * (1f - compensation);
                var compensationMultiplier = targetMultiplier / baselineMultiplier;
                args.AngleIncrease *= compensationMultiplier;
                args.MaxAngle *= compensationMultiplier;
            }
        }

        var bounded = Wh40kClassRuntimePolicy.NormalizeGunModifiers(new Wh40kClassGunModifierValues(
            args.FireRate,
            args.CameraRecoilScalar,
            args.AngleIncrease,
            args.AngleDecay,
            args.MinAngle,
            args.MaxAngle,
            args.ProjectileSpeed));
        args.FireRate = bounded.FireRate;
        args.CameraRecoilScalar = bounded.CameraRecoilScalar;
        args.AngleIncrease = bounded.AngleIncrease;
        args.AngleDecay = bounded.AngleDecay;
        args.MinAngle = bounded.MinAngle;
        args.MaxAngle = bounded.MaxAngle;
        args.ProjectileSpeed = bounded.ProjectileSpeed;
    }

    private void OnRefreshMovement(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        var bonus = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.MovementModifier, null) / 100f;
        if (bonus != 0f)
            args.ModifySpeed(Math.Clamp(1f + bonus, 0.5f, 1.5f));
        if (ent.Comp.RuntimeStates.Any(pair => pair.Value.ExpiresAt > _timing.CurTime &&
                                                  ent.Comp.ActiveEffects.TryGetValue(pair.Key, out var effect) &&
                                                  effect.Mechanic is Wh40kClassRuntimeMechanic.StationaryStance or
                                                      Wh40kClassRuntimeMechanic.FirePosition))
        {
            args.ModifySpeed(0.5f);
        }

        if (TryComp<Wh40kClassDashSpeedRuntimeComponent>(ent, out var dashSpeed) &&
            dashSpeed.EndsAt > _timing.CurTime)
        {
            args.ModifySpeed(1f + Math.Clamp(dashSpeed.BonusPercent, 0, 25) / 100f);
        }

        if (_weaponHandling.TryGetHeldHandling(ent.Owner, out var heldWeapon, out var heldHandling))
        {
            var heldPenalty = Wh40kClassWeaponHandlingSystem.GetValues(heldHandling.Category).HeldPenaltyPercent;
            var compensation = Math.Clamp(
                GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.GunHeldPenaltyCompensation, heldWeapon),
                0,
                75) / 100f;
            if (heldPenalty > 0 && compensation > 0f)
                args.ModifySpeed(Wh40kClassRuntimePolicy.GetPenaltyCompensationMultiplier(heldPenalty, compensation));
        }

        if (_weaponHandling.TryGetShotPenalty(ent.Owner, out var shotWeapon, out var shotValues))
        {
            var compensation = Math.Clamp(
                GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.GunShotPenaltyCompensation, shotWeapon),
                0,
                50) / 100f;
            if (compensation > 0f)
                args.ModifySpeed(Wh40kClassRuntimePolicy.GetPenaltyCompensationMultiplier(shotValues.ShotPenaltyPercent, compensation));
        }
    }

    private void OnMeleeHit(Entity<Wh40kClassEquipmentRelayComponent> ent, ref MeleeHitEvent args)
    {
        if (args.User != ent.Comp.Body || !TryComp(ent.Comp.Body, out Wh40kClassRuntimeProfileComponent? profile))
            return;

        if (args.IsHit && args.HitEntities.Count == 1)
        {
            var target = args.HitEntities[0];
            var bonusPercent = 0;
            foreach (var effect in profile.ActiveEffects.Values.Where(effect =>
                         effect.Mechanic == Wh40kClassRuntimeMechanic.MeleeDamageModifier &&
                         (!effect.RequiresEquipment || effect.SupportingItem == ent.Owner)))
            {
                var conditional = effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresInjuredTarget) ||
                                  effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresMarkedTarget) ||
                                  effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresBackstab) ||
                                  effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresCloak);
                if (!conditional)
                    continue;
                if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresInjuredTarget) &&
                    (!TryComp<DamageableComponent>(target, out var damageable) || damageable.TotalDamage <= 0))
                {
                    continue;
                }
                if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresMarkedTarget) && !HasMark(profile, target))
                    continue;
                if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresBackstab) &&
                    !_backstab.TryBackstab(target, ent.Comp.Body, Angle.FromDegrees(45), false, false, false))
                    continue;
                if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresCloak) && !HasCloakState(profile))
                    continue;

                bonusPercent += Math.Max(0, effect.Magnitude);
            }
            foreach (var (effectId, state) in profile.RuntimeStates.ToArray())
            {
                if (!profile.ActiveEffects.TryGetValue(effectId, out var effect) ||
                    effect.Mechanic is not (Wh40kClassRuntimeMechanic.AttackPreparation or
                        Wh40kClassRuntimeMechanic.NpcPressure) ||
                    effect.SupportingItem != ent.Owner ||
                    state.ExpiresAt <= _timing.CurTime ||
                    state.Target is { } stateTarget && stateTarget != target ||
                    !_safety.IsClassActionAllowed(ent.Comp.Body, target, effect.Safety))
                {
                    continue;
                }

                if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresMarkedTarget) && !HasMark(profile, target))
                    continue;
                if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresBackstab) &&
                    !_backstab.TryBackstab(target, ent.Comp.Body, Angle.FromDegrees(45), false, false, false))
                    continue;
                if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresCloak) && !HasCloakState(profile))
                    continue;

                if (effect.Mechanic == Wh40kClassRuntimeMechanic.AttackPreparation &&
                    effect.Safety == Wh40kClassEffectSafety.OffensiveStamina)
                {
                    _stamina.TakeStaminaDamage(target, Math.Clamp(effect.Magnitude, 5, 50), source: ent.Comp.Body, with: ent.Owner);
                    RemoveRuntimeState((ent.Comp.Body, profile), effectId);
                    continue;
                }
                if (effect.Mechanic == Wh40kClassRuntimeMechanic.AttackPreparation &&
                    effect.Safety == Wh40kClassEffectSafety.OffensiveControl)
                {
                    ApplyRuntimeSlow(ent.Comp.Body, target, effect.EffectId, effect.Magnitude, effect.Duration);
                    RemoveRuntimeState((ent.Comp.Body, profile), effectId);
                    continue;
                }

                bonusPercent += Math.Max(0, effect.Magnitude);
                RemoveRuntimeState((ent.Comp.Body, profile), effectId);
            }

            if (TryComp<Wh40kClassWeaponCoatingComponent>(ent.Owner, out var coating) &&
                coating.Source == ent.Comp.Body &&
                coating.ExpiresAt > _timing.CurTime &&
                coating.Charges > 0 &&
                _safety.IsClassActionAllowed(ent.Comp.Body, target, Wh40kClassEffectSafety.OffensiveDamage) &&
                _prototypes.TryIndex(PoisonDamageType, out DamageTypePrototype? poisonDamage))
            {
                args.BonusDamage += new DamageSpecifier(poisonDamage, Math.Clamp(coating.Magnitude, 1, 20));
                coating.Charges--;
                if (coating.Charges <= 0)
                    RemCompDeferred<Wh40kClassWeaponCoatingComponent>(ent.Owner);
            }

            if (bonusPercent > 0)
                args.BonusDamage += args.BaseDamage * MathF.Min(bonusPercent / 100f, MaximumPassiveBonus);
        }

        BreakStates((ent.Comp.Body, profile), Wh40kClassCounterplay.BreakOnAttack);
    }

    private void OnAttacked(Entity<Wh40kClassRuntimeProfileComponent> ent, ref AttackedEvent args)
    {
        if (!Exists(args.User) || args.User == ent.Owner || !TryComp<PhysicsComponent>(args.User, out var physics))
            return;

        foreach (var effect in ent.Comp.ActiveEffects.Values.Where(effect =>
                     effect.Mechanic == Wh40kClassRuntimeMechanic.KnockbackReaction &&
                     (!effect.RequiresEquipment || effect.SupportingItem is { Valid: true }) &&
                     ent.Comp.CooldownEnds.GetValueOrDefault(effect.EffectId) <= _timing.CurTime))
        {
            var source = _transform.GetMapCoordinates(ent.Owner);
            var attacker = _transform.GetMapCoordinates(args.User);
            if (source.MapId != attacker.MapId)
                continue;
            var direction = attacker.Position - source.Position;
            if (direction.LengthSquared() < 0.01f ||
                !_safety.IsClassActionAllowed(ent.Owner, args.User, Wh40kClassEffectSafety.OffensiveControl))
            {
                continue;
            }

            _physics.ApplyLinearImpulse(
                args.User,
                Vector2.Normalize(direction) * physics.Mass * Math.Clamp(effect.Magnitude / 10f, 0.5f, 2.5f),
                body: physics);
            CommitAction(ent.Comp, effect);
        }
    }

    private void OnBeforeGunShot(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref SelfBeforeGunShotEvent args)
    {
        if (args.Cancelled)
            return;

        PrepareSoldierShotModifiers(ent, args);
        BreakStates(ent, Wh40kClassCounterplay.BreakOnAttack);
    }

    private void OnGunShot(Entity<Wh40kClassEquipmentRelayComponent> ent, ref GunShotEvent args)
    {
        if (args.User != ent.Comp.Body || !TryComp(ent.Comp.Body, out Wh40kClassRuntimeProfileComponent? profile))
            return;

        foreach (var effect in profile.ActiveEffects.Values.Where(effect =>
                     effect.Mechanic is Wh40kClassRuntimeMechanic.AreaPressure or Wh40kClassRuntimeMechanic.SuppressionMode &&
                     (effect.Mechanic != Wh40kClassRuntimeMechanic.SuppressionMode ||
                      profile.RuntimeStates.GetValueOrDefault(effect.EffectId)?.ExpiresAt > _timing.CurTime) &&
                     (!effect.RequiresEquipment || effect.SupportingItem == ent.Owner)))
        {
            ApplyAreaPressure(ent.Comp.Body, args.ToCoordinates, effect);
        }

        if (TryComp<GunComponent>(ent, out var gun) && gun.SelectedMode == SelectiveFire.SemiAuto &&
            profile.ActiveEffects.Values.Any(effect =>
                effect.Mechanic == Wh40kClassRuntimeMechanic.DashShotCostReduction &&
                (!effect.RequiresEquipment || effect.SupportingItem == ent.Owner)))
        {
            profile.LastMarksmanShotAt = _timing.CurTime;
        }
    }

    private void OnAmmoShot(Entity<Wh40kClassEquipmentRelayComponent> ent, ref AmmoShotEvent args)
    {
        if (args.User != ent.Comp.Body ||
            !TryComp<Wh40kClassPendingShotComponent>(ent, out var pending) ||
            pending.Body != ent.Comp.Body ||
            pending.ExpiresAt <= _timing.CurTime)
        {
            return;
        }

        foreach (var projectile in args.FiredProjectiles)
            ApplyShotModifier(projectile, pending);
        RemComp<Wh40kClassPendingShotComponent>(ent);
    }

    private void OnGunReloadSlotInsert(Entity<GunComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Cancelled || args.User is not { Valid: true } user || !HasActiveBarrage(user))
            return;

        args.Cancelled = true;
    }

    private void OnGunReloadInteractUsing(Entity<GunComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            !HasActiveBarrage(args.User) ||
            !HasComp<BallisticAmmoProviderComponent>(ent) && !HasComp<RevolverAmmoProviderComponent>(ent))
        {
            return;
        }

        args.Handled = true;
    }

    private bool HasActiveBarrage(EntityUid user)
    {
        return TryComp<Wh40kClassRuntimeProfileComponent>(user, out var profile) &&
               profile.RuntimeStates.Any(pair =>
                   pair.Value.ExpiresAt > _timing.CurTime &&
                   profile.ActiveEffects.TryGetValue(pair.Key, out var effect) &&
                   effect.Mechanic == Wh40kClassRuntimeMechanic.Barrage);
    }

    private void PrepareSoldierShotModifiers(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        SelfBeforeGunShotEvent args)
    {
        if (args.Gun.Comp.SelectedMode != SelectiveFire.SemiAuto)
            return;

        var lowHealthBonus = GetCappedMagnitude(ent.Comp, Wh40kClassRuntimeMechanic.LowHealthGunDamageBonus, args.Gun.Owner);
        var priorityTargetBonus = ent.Comp.RuntimeStates
            .Where(pair => pair.Value.ExpiresAt > _timing.CurTime &&
                           ent.Comp.ActiveEffects.TryGetValue(pair.Key, out var effect) &&
                           effect.Mechanic == Wh40kClassRuntimeMechanic.TargetMark)
            .Select(pair => Math.Max(0, ent.Comp.ActiveEffects[pair.Key].Magnitude))
            .DefaultIfEmpty()
            .Max();
        var hitConfirmation = ent.Comp.ActiveEffects.Values.Any(effect =>
            effect.Mechanic == Wh40kClassRuntimeMechanic.HitConfirmation &&
            (!effect.RequiresEquipment || effect.SupportingItem == args.Gun.Owner));
        var armorMultiplier = 1f;
        foreach (var (effectId, state) in ent.Comp.RuntimeStates.ToArray())
        {
            if (state.ExpiresAt <= _timing.CurTime ||
                !ent.Comp.ActiveEffects.TryGetValue(effectId, out var verdict) ||
                verdict.Mechanic != Wh40kClassRuntimeMechanic.VerdictShot ||
                verdict.SupportingItem != args.Gun.Owner)
            {
                continue;
            }

            armorMultiplier = 1.25f;
            var remaining = state.Charges - 1;
            if (remaining <= 0)
                RemoveRuntimeState(ent, effectId);
            else
                ent.Comp.RuntimeStates[effectId] = state with { Charges = remaining };
            break;
        }

        if (lowHealthBonus <= 0 && priorityTargetBonus <= 0 && !hitConfirmation && armorMultiplier <= 1f)
            return;

        var pending = EnsureComp<Wh40kClassPendingShotComponent>(args.Gun.Owner);
        pending.Body = ent.Owner;
        pending.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        pending.LowHealthDamageBonus = Math.Clamp(lowHealthBonus, 0, 50) / 100f;
        pending.PriorityTargetDamageBonus = Math.Clamp(priorityTargetBonus, 0, 50) / 100f;
        pending.HitConfirmation = hitConfirmation;
        pending.ArmorPenetrationMultiplier = armorMultiplier;

        foreach (var (ammo, _) in args.Ammo)
        {
            if (ammo is { Valid: true } ammoUid && HasComp<HitscanBasicDamageComponent>(ammoUid))
                ApplyShotModifier(ammoUid, pending);
        }
    }

    private void ApplyShotModifier(EntityUid uid, Wh40kClassPendingShotComponent pending)
    {
        var modifier = EnsureComp<Wh40kClassShotModifierComponent>(uid);
        modifier.Body = pending.Body;
        modifier.LowHealthDamageBonus = pending.LowHealthDamageBonus;
        modifier.PriorityTargetDamageBonus = pending.PriorityTargetDamageBonus;
        modifier.HitConfirmation = pending.HitConfirmation;
        modifier.ArmorPenetrationMultiplier = pending.ArmorPenetrationMultiplier;
        if (TryComp<ProjectileComponent>(uid, out var projectile) && modifier.ArmorPenetrationMultiplier > 1f)
            projectile.ArmorPenetration *= modifier.ArmorPenetrationMultiplier;
    }

    private void OnShotModifierProjectileHit(Entity<Wh40kClassShotModifierComponent> ent, ref ProjectileHitEvent args)
    {
        if (!HasComp<MobStateComponent>(args.Target))
            return;

        if (ent.Comp.LowHealthDamageBonus > 0f && IsBelowHealthThreshold(args.Target, 0.7f))
            args.Damage *= 1f + ent.Comp.LowHealthDamageBonus;
        if (HasPriorityTarget(ent.Comp, args.Target))
            args.Damage *= 1f + ent.Comp.PriorityTargetDamageBonus;
        PlayHitConfirmation(ent.Comp);
    }

    private void OnShotModifierHitscanRaycast(Entity<Wh40kClassShotModifierComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled || args.HitEntity is not { } target || !TryComp<HitscanBasicDamageComponent>(ent, out var damage))
            return;

        if (ent.Comp.LowHealthDamageBonus > 0f && IsBelowHealthThreshold(target, 0.7f))
            damage.Damage *= 1f + ent.Comp.LowHealthDamageBonus;
        if (HasPriorityTarget(ent.Comp, target))
            damage.Damage *= 1f + ent.Comp.PriorityTargetDamageBonus;
        if (ent.Comp.ArmorPenetrationMultiplier > 1f)
            damage.ArmorPenetration *= ent.Comp.ArmorPenetrationMultiplier;
    }

    private void OnShotModifierHitscanDamage(Entity<Wh40kClassShotModifierComponent> ent, ref HitscanDamageDealtEvent args)
    {
        if (HasComp<MobStateComponent>(args.Target))
            PlayHitConfirmation(ent.Comp);
    }

    private bool IsBelowHealthThreshold(EntityUid target, float remainingFraction)
    {
        return TryComp<DamageableComponent>(target, out var damageable) &&
               TryComp<MobThresholdsComponent>(target, out var thresholds) &&
               _mobThresholds.TryGetThresholdForState(target, MobState.Critical, out var critical, thresholds) &&
               critical is { } threshold &&
               damageable.TotalDamage.Float() > threshold.Float() * (1f - remainingFraction);
    }

    private bool HasPriorityTarget(Wh40kClassShotModifierComponent modifier, EntityUid target)
    {
        return modifier.PriorityTargetDamageBonus > 0f &&
               TryComp<Wh40kClassRuntimeProfileComponent>(modifier.Body, out var profile) &&
               HasMark(profile, target);
    }

    private void PlayHitConfirmation(Wh40kClassShotModifierComponent modifier)
    {
        if (!modifier.HitConfirmation ||
            !TryComp<Wh40kClassRuntimeProfileComponent>(modifier.Body, out var profile) ||
            !_players.TryGetSessionById(profile.UserId, out var session))
        {
            return;
        }

        _audio.PlayGlobal("/Audio/Machines/high_tech_confirm.ogg", session);
    }

    private void OnMove(Entity<Wh40kClassRuntimeProfileComponent> ent, ref MoveEvent args)
    {
        if (args.NewPosition == args.OldPosition)
            return;

        ent.Comp.LastStationaryResetAt = _timing.CurTime;
        BreakStates(ent, Wh40kClassCounterplay.BreakOnMove);
    }

    private void OnProfileRemoved(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref ComponentRemove args)
    {
        foreach (var effectId in ent.Comp.RuntimeStates.Keys.ToArray())
            RemoveRuntimeState(ent, effectId);
        if (TryComp<Wh40kClassDashRuntimeComponent>(ent, out _))
            RemComp<Wh40kClassDashRuntimeComponent>(ent);
        if (TryComp<Wh40kClassDashSpeedRuntimeComponent>(ent, out _))
            RemComp<Wh40kClassDashSpeedRuntimeComponent>(ent);
        if (TryComp<Wh40kClassNestRouteComponent>(ent, out _))
            RemComp<Wh40kClassNestRouteComponent>(ent);
        RemoveCommandStatesFromSource(ent.Owner);
        RemoveAssassinItemEffects(ent.Owner, null);
        EndCloak(ent.Owner);
    }

    private void OnProfileReconciled(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        ref Wh40kClassProfileReconciledEvent args)
    {
        ent.Comp.LastStationaryResetAt = _timing.CurTime;
        foreach (var effectId in ent.Comp.RuntimeStates.Keys
                     .Where(effectId => !ent.Comp.ActiveEffects.ContainsKey(effectId))
                     .ToArray())
        {
            RemoveRuntimeState(ent, effectId);
        }

        foreach (var effectId in ent.Comp.CooldownEnds.Keys
                     .Where(effectId => !ent.Comp.ActiveEffects.ContainsKey(effectId))
                     .ToArray())
        {
            ent.Comp.CooldownEnds.Remove(effectId);
        }

        if (TryComp<Wh40kClassNestRouteComponent>(ent, out var route) &&
            !ent.Comp.ActiveEffects.ContainsKey(route.EffectId))
        {
            RemComp<Wh40kClassNestRouteComponent>(ent);
        }

        var pressures = EntityQueryEnumerator<Wh40kClassNpcPressureComponent>();
        while (pressures.MoveNext(out var uid, out var pressure))
        {
            foreach (var source in pressure.Sources.Keys
                         .Where(source => source.Source == ent.Owner && !ent.Comp.ActiveEffects.ContainsKey(source.EffectId))
                         .ToArray())
            {
                RemoveNpcPressureSource((uid, pressure), source);
            }

            if (pressure.Sources.Count == 0)
                RemCompDeferred<Wh40kClassNpcPressureComponent>(uid);
        }

        if (!ent.Comp.ActiveEffects.Values.Any(effect => effect.Mechanic == Wh40kClassRuntimeMechanic.Cloak))
            EndCloak(ent.Owner);

        RemoveCommandStatesFromSource(ent.Owner, ent.Comp.ActiveEffects.Keys);

        RemoveAssassinItemEffects(ent.Owner, ent.Comp.ActiveEffects.Keys);
    }

    private void RemoveAssassinItemEffects(EntityUid source, ICollection<string>? activeEffects)
    {
        var thrownEffects = EntityQueryEnumerator<Wh40kClassThrownEffectComponent>();
        while (thrownEffects.MoveNext(out var item, out var thrown))
        {
            if (thrown.Source == source && (activeEffects == null || !activeEffects.Contains(thrown.EffectId)))
                RemCompDeferred<Wh40kClassThrownEffectComponent>(item);
        }

        var coatings = EntityQueryEnumerator<Wh40kClassWeaponCoatingComponent>();
        while (coatings.MoveNext(out var item, out var coating))
        {
            if (coating.Source == source && (activeEffects == null || !activeEffects.Contains(coating.EffectId)))
                RemCompDeferred<Wh40kClassWeaponCoatingComponent>(item);
        }
    }

    private void OnPressureMovement(
        Entity<Wh40kClassNpcPressureComponent> ent,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        var magnitude = ent.Comp.Sources.Values
            .Where(state => state.Category is Wh40kClassModifierCategory.Mobility or
                Wh40kClassModifierCategory.Command or Wh40kClassModifierCategory.Core)
            .Select(state => state.Magnitude)
            .DefaultIfEmpty()
            .Max();
        if (magnitude > 0)
            args.ModifySpeed(Math.Clamp(1f - magnitude / 100f, 0.35f, 1f));
    }

    private void OnPressureGun(
        Entity<Wh40kClassNpcPressureComponent> ent,
        ref GunRefreshModifiersEvent args)
    {
        var magnitude = ent.Comp.Sources.Values
            .Where(state => state.Category is Wh40kClassModifierCategory.Offense or
                Wh40kClassModifierCategory.Command or Wh40kClassModifierCategory.Core)
            .Select(state => state.Magnitude)
            .DefaultIfEmpty()
            .Max();
        if (magnitude <= 0)
            return;

        var multiplier = 1f + MathF.Min(magnitude / 100f, 1f);
        args.AngleIncrease *= multiplier;
        args.MinAngle *= multiplier;
        args.MaxAngle *= multiplier;
    }

    private void OnPartyChanged(Wh40kPartyRecord? _)
    {
        // The old revision must stop affecting bodies before another combat event can consume it.
        ClearCommandRecipients();
        _nextCommandRefresh = TimeSpan.Zero;
    }

    private void RefreshCommandRecipients()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var now = _timing.CurTime;
        var candidates = BuildCommandCandidates();
        var pending = new Dictionary<EntityUid, List<Wh40kClassCommandRecipientState>>();
        var profiles = EntityQueryEnumerator<Wh40kClassRuntimeProfileComponent>();
        while (profiles.MoveNext(out var source, out var profile))
        {
            if (!IsLiving(source) ||
                !_players.Sessions.Any(session => session.UserId == profile.UserId && session.AttachedEntity == source))
            {
                continue;
            }

            var sourceCoordinates = _transform.GetMapCoordinates(source);
            IReadOnlySet<Robust.Shared.Network.NetUserId>? partyMembers = null;
            Guid? partyId = null;
            long partyRevision = 0;
            if (_parties.TryGetParty(profile.UserId, out var party))
            {
                partyId = party.Id;
                partyRevision = party.Revision;
                partyMembers = party.Members.Select(member => member.UserId).ToHashSet();
            }

            var recipients = Wh40kClassRuntimePolicy.SelectCommandRecipients(
                sourceCoordinates.MapId,
                sourceCoordinates.Position,
                candidates,
                partyMembers);
            foreach (var effect in profile.ActiveEffects.Values.Where(IsOverseerCommandEffect))
            {
                Wh40kClassRuntimeState? runtimeState = null;
                if (effect.Action != null &&
                    (!profile.RuntimeStates.TryGetValue(effect.EffectId, out runtimeState) ||
                     runtimeState.ExpiresAt <= now))
                {
                    continue;
                }

                var expiresAt = runtimeState == null
                    ? now + CommandLeaseDuration
                    : TimeSpan.FromTicks(Math.Min(runtimeState.ExpiresAt.Ticks, (now + CommandLeaseDuration).Ticks));
                foreach (var recipient in recipients)
                {
                    if (!_safety.IsClassActionAllowed(source, recipient.Body, Wh40kClassEffectSafety.AreaEffect))
                        continue;

                    if (!pending.TryGetValue(recipient.Body, out var states))
                    {
                        states = new List<Wh40kClassCommandRecipientState>();
                        pending.Add(recipient.Body, states);
                    }
                    states.Add(new Wh40kClassCommandRecipientState(
                        source,
                        profile.UserId,
                        effect.EffectId,
                        effect.ModifierCategory,
                        effect.Mechanic,
                        Math.Clamp(Math.Abs(effect.Magnitude), 0, 35),
                        expiresAt,
                        runtimeState?.Target,
                        partyId,
                        partyRevision));
                }
            }
        }

        var existing = EntityQueryEnumerator<Wh40kClassCommandRecipientComponent>();
        while (existing.MoveNext(out var uid, out var command))
        {
            if (pending.ContainsKey(uid))
                continue;

            var refreshMovement = command.Categories.ContainsKey(Wh40kClassModifierCategory.Mobility);
            command.Categories.Clear();
            RemCompDeferred<Wh40kClassCommandRecipientComponent>(uid);
            if (refreshMovement)
                _movement.RefreshMovementSpeedModifiers(uid);
        }

        foreach (var (recipient, states) in pending)
        {
            var command = EnsureComp<Wh40kClassCommandRecipientComponent>(recipient);
            command.Categories.Clear();
            foreach (var state in Wh40kClassRuntimePolicy.SelectStrongestCommands(states, now))
                command.Categories[state.Category] = state;
            _movement.RefreshMovementSpeedModifiers(recipient);
        }

        Wh40kClassMetrics.ObserveCommandRefresh(pending.Count, candidates.Count, stopwatch.Elapsed.TotalSeconds);
    }

    internal void RefreshCommandRecipientsForTest()
    {
        RefreshCommandRecipients();
    }

    private IReadOnlyList<Wh40kCommandRecipientCandidate> BuildCommandCandidates()
    {
        var candidates = new List<Wh40kCommandRecipientCandidate>();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } body || !Exists(body))
                continue;

            var coordinates = _transform.GetMapCoordinates(body);
            candidates.Add(new Wh40kCommandRecipientCandidate(
                session.UserId,
                body,
                coordinates.MapId,
                coordinates.Position,
                IsLiving(body)));
        }

        return candidates;
    }

    private bool ApplyStaminaCommand(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        Wh40kResolvedClassEffect effect)
    {
        if (!StartRuntimeState(ent, effect, null, Transform(ent).Coordinates))
            return false;

        var sourceCoordinates = _transform.GetMapCoordinates(ent.Owner);
        IReadOnlySet<Robust.Shared.Network.NetUserId>? partyMembers = null;
        if (_parties.TryGetParty(ent.Comp.UserId, out var party))
            partyMembers = party.Members.Select(member => member.UserId).ToHashSet();

        var recipients = Wh40kClassRuntimePolicy.SelectCommandRecipients(
            sourceCoordinates.MapId,
            sourceCoordinates.Position,
            BuildCommandCandidates(),
            partyMembers);
        foreach (var recipient in recipients)
        {
            if (_safety.IsClassActionAllowed(ent.Owner, recipient.Body, Wh40kClassEffectSafety.AreaEffect))
                _stamina.TakeStaminaDamage(recipient.Body, -Math.Clamp(Math.Abs(effect.Magnitude), 1, 30), source: ent.Owner);
        }

        RefreshCommandRecipients();
        return recipients.Count > 0;
    }

    private bool DeployCommandBeacon(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        Wh40kResolvedClassEffect effect)
    {
        var consumable = _hands.EnumerateHeld(ent.Owner)
            .FirstOrDefault(item => _tags.HasTag(item, CommandBeaconConsumableTag));
        if (!consumable.IsValid())
            return false;

        var beacon = Spawn(CommandBeaconPrototype, Transform(ent).Coordinates);
        var runtime = EnsureComp<Wh40kClassCommandBeaconComponent>(beacon);
        runtime.Source = ent.Owner;
        runtime.ExpiresAt = _timing.CurTime + effect.Duration;
        QueueDel(consumable);
        return StartRuntimeState(ent, effect, beacon, Transform(ent).Coordinates);
    }

    private void OnCommandBeforeDamage(
        Entity<Wh40kClassCommandRecipientComponent> ent,
        ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !TryGetCurrentCommand(ent.Owner, ent.Comp, Wh40kClassModifierCategory.Defense, out var state))
            return;

        args.Damage *= 1f - Math.Clamp(state.Magnitude / 100f, 0f, 0.25f);
    }

    private void OnCommandBeforeStaminaDamage(
        Entity<Wh40kClassCommandRecipientComponent> ent,
        ref BeforeStaminaDamageEvent args)
    {
        if (args.Cancelled || !TryGetCurrentCommand(ent.Owner, ent.Comp, Wh40kClassModifierCategory.Defense, out var state))
            return;

        args.Value *= 1f - Math.Clamp(state.Magnitude / 100f, 0f, 0.35f);
    }

    private void OnCommandGun(
        Entity<Wh40kClassCommandRecipientComponent> ent,
        ref GunRefreshModifiersEvent args)
    {
        if (!TryGetCurrentCommand(ent.Owner, ent.Comp, Wh40kClassModifierCategory.Offense, out var offense) &&
            !TryGetCurrentCommand(ent.Owner, ent.Comp, Wh40kClassModifierCategory.Command, out offense))
        {
            return;
        }

        var multiplier = 1f - Math.Clamp(offense.Magnitude / 100f, 0f, 0.25f);
        args.CameraRecoilScalar *= multiplier;
        args.AngleIncrease *= multiplier;
        args.MinAngle *= multiplier;
        args.MaxAngle *= multiplier;
    }

    private void OnCommandMovement(
        Entity<Wh40kClassCommandRecipientComponent> ent,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryGetCurrentCommand(ent.Owner, ent.Comp, Wh40kClassModifierCategory.Mobility, out var state))
            return;

        args.ModifySpeed(1f + Math.Clamp(state.Magnitude / 100f, 0f, 0.2f));
    }

    private bool TryGetCurrentCommand(
        EntityUid recipient,
        Wh40kClassCommandRecipientComponent command,
        Wh40kClassModifierCategory category,
        out Wh40kClassCommandRecipientState state)
    {
        if (!command.Categories.TryGetValue(category, out state!) ||
            state.ExpiresAt <= _timing.CurTime ||
            !Exists(state.Source) ||
            !IsLiving(state.Source) ||
            !IsLiving(recipient) ||
            !_safety.IsClassActionAllowed(state.Source, recipient, Wh40kClassEffectSafety.AreaEffect))
        {
            return false;
        }

        var sourceCoordinates = _transform.GetMapCoordinates(state.Source);
        var recipientCoordinates = _transform.GetMapCoordinates(recipient);
        if (sourceCoordinates.MapId != recipientCoordinates.MapId ||
            Vector2.DistanceSquared(sourceCoordinates.Position, recipientCoordinates.Position) >
            Wh40kClassRuntimePolicy.OverseerCommandRadius * Wh40kClassRuntimePolicy.OverseerCommandRadius)
        {
            return false;
        }

        var session = _players.Sessions.FirstOrDefault(candidate => candidate.AttachedEntity == recipient);
        if (session == null)
            return false;
        if (state.PartyId == null)
            return !_parties.TryGetParty(state.SourceUserId, out _);
        return _parties.TryGetParty(state.SourceUserId, out var party) &&
               party.Id == state.PartyId &&
               party.Revision == state.PartyRevision &&
               party.Members.Any(member => member.UserId == session.UserId);
    }

    private void RemoveCommandStatesFromSource(EntityUid source, ICollection<string>? activeEffects = null)
    {
        var recipients = EntityQueryEnumerator<Wh40kClassCommandRecipientComponent>();
        while (recipients.MoveNext(out var uid, out var command))
        {
            foreach (var category in command.Categories
                         .Where(pair => pair.Value.Source == source &&
                                        (activeEffects == null || !activeEffects.Contains(pair.Value.EffectId)))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                command.Categories.Remove(category);
            }

            if (command.Categories.Count == 0)
                RemCompDeferred<Wh40kClassCommandRecipientComponent>(uid);
            _movement.RefreshMovementSpeedModifiers(uid);
        }
    }

    private void ClearCommandRecipients()
    {
        var recipients = EntityQueryEnumerator<Wh40kClassCommandRecipientComponent>();
        while (recipients.MoveNext(out var uid, out var command))
        {
            command.Categories.Clear();
            RemCompDeferred<Wh40kClassCommandRecipientComponent>(uid);
            _movement.RefreshMovementSpeedModifiers(uid);
        }
    }

    private static bool IsOverseerCommandEffect(Wh40kResolvedClassEffect effect)
    {
        return effect.Mechanic is Wh40kClassRuntimeMechanic.CommandAura or Wh40kClassRuntimeMechanic.CommandStamina or
                   Wh40kClassRuntimeMechanic.CommandBeacon ||
               effect.Mechanic == Wh40kClassRuntimeMechanic.TargetMark &&
               effect.EffectId.StartsWith("effect-overseer-formation-doctrine-", StringComparison.Ordinal);
    }

    private void OnInterceptedDamage(
        Entity<Wh40kClassInterceptTargetComponent> ent,
        ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled)
            return;

        var source = ent.Comp.Sources
            .Where(pair => pair.Value.ExpiresAt > _timing.CurTime &&
                           Exists(pair.Key.Source) &&
                           IsLiving(pair.Key.Source) &&
                           _safety.IsClassActionAllowed(pair.Key.Source, ent.Owner, Wh40kClassEffectSafety.SelfOnly))
            .OrderByDescending(pair => pair.Value.Magnitude)
            .FirstOrDefault();
        if (source.Key == default)
            return;

        var fraction = Math.Clamp(source.Value.Magnitude / 100f, 0.1f, 0.6f);
        var diverted = args.Damage * fraction;
        args.Damage *= 1f - fraction;
        _damageable.TryChangeDamage(
            source.Key.Source,
            diverted,
            origin: args.Origin,
            interruptsDoAfters: true,
            canSever: false);
    }

    private bool TryResolveAction(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityUid action,
        EntityUid? target,
        out Wh40kResolvedClassEffect effect)
    {
        effect = default!;
        if (!TryComp<Wh40kClassGrantedActionComponent>(action, out var marker) ||
            marker.Body != ent.Owner ||
            !ent.Comp.ActiveEffects.TryGetValue(marker.EffectId, out var resolved) ||
            resolved.Action == null ||
            !IsLiving(ent.Owner) ||
            ent.Comp.CooldownEnds.GetValueOrDefault(resolved.EffectId) > _timing.CurTime ||
            !_safety.IsClassActionAllowed(ent.Owner, target, resolved.Safety) ||
            !HasLiveEquipment(ent.Owner, resolved))
        {
            return false;
        }

        effect = resolved;

        if (target is not { Valid: true } targetUid)
            return true;
        if (!Exists(targetUid) || targetUid == ent.Owner && effect.Safety != Wh40kClassEffectSafety.SelfOnly)
            return false;
        if (effect.Range > 0f && !_interaction.InRangeUnobstructed(ent.Owner, targetUid, effect.Range))
            return false;
        if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.NpcOnly) && !HasComp<ActiveNPCComponent>(targetUid))
            return false;
        if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresDownedTarget) &&
            (!TryComp<MobStateComponent>(targetUid, out var mob) || mob.CurrentState == MobState.Alive))
        {
            return false;
        }
        if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresMarkedTarget) && !HasMark(ent.Comp, targetUid))
            return false;
        if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresInjuredTarget) &&
            (!TryComp<DamageableComponent>(targetUid, out var damageable) || damageable.TotalDamage <= 0))
        {
            return false;
        }
        if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresBackstab) &&
            !_backstab.TryBackstab(targetUid, ent.Owner, Angle.FromDegrees(60), false, false, false))
        {
            return false;
        }
        if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresCloak) && !HasCloakState(ent.Comp))
            return false;
        if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresAlly) &&
            !_npcFaction.IsEntityFriendly(ent.Owner, targetUid))
        {
            return false;
        }
        if (effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresHostile) &&
            _npcFaction.IsEntityFriendly(ent.Owner, targetUid))
        {
            return false;
        }

        return true;
    }

    private bool HasLiveEquipment(EntityUid body, Wh40kResolvedClassEffect effect)
    {
        if (!effect.RequiresEquipment)
            return true;
        if (effect.SupportingItem is not { Valid: true } item)
            return false;

        return _runtime.BuildEquipmentSnapshot(body).Any(snapshot => snapshot.Entity == item);
    }

    private static bool HasMark(Wh40kClassRuntimeProfileComponent profile, EntityUid target)
    {
        return profile.RuntimeStates.Any(pair =>
            pair.Value.Target == target &&
            profile.ActiveEffects.TryGetValue(pair.Key, out var effect) &&
            effect.Mechanic == Wh40kClassRuntimeMechanic.TargetMark);
    }

    private static bool HasCloakState(Wh40kClassRuntimeProfileComponent profile)
    {
        return profile.RuntimeStates.Any(pair =>
            profile.ActiveEffects.TryGetValue(pair.Key, out var effect) &&
            effect.Mechanic == Wh40kClassRuntimeMechanic.Cloak);
    }

    private bool StartRuntimeState(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        Wh40kResolvedClassEffect effect,
        EntityUid? target,
        EntityCoordinates? origin = null)
    {
        if (effect.Duration <= TimeSpan.Zero)
            return false;

        ent.Comp.RuntimeStates[effect.EffectId] = new Wh40kClassRuntimeState(
            target,
            _timing.CurTime + effect.Duration,
            origin);
        return true;
    }

    private bool StartIntercept(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityUid target,
        Wh40kResolvedClassEffect effect)
    {
        if (target == ent.Owner || effect.Duration <= TimeSpan.Zero || !StartRuntimeState(ent, effect, target))
            return false;

        var intercept = EnsureComp<Wh40kClassInterceptTargetComponent>(target);
        intercept.Sources[new Wh40kClassInterceptSource(ent.Owner, effect.EffectId)] =
            new Wh40kClassInterceptState(_timing.CurTime + effect.Duration, Math.Clamp(effect.Magnitude, 10, 60));
        return true;
    }

    private bool StartDashToEntity(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityUid target,
        Wh40kResolvedClassEffect effect)
    {
        if (!StartDash(ent, Transform(target).Coordinates, effect))
            return false;

        return !effect.Counterplay.HasFlag(Wh40kClassCounterplay.RequiresAlly) || StartIntercept(ent, target, effect);
    }

    private bool StartDash(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityCoordinates target,
        Wh40kResolvedClassEffect effect,
        bool allowMarkedLowObstacles = false)
    {
        if (effect.Duration <= TimeSpan.Zero ||
            !IsLiving(ent.Owner) ||
            !_actionBlocker.CanMove(ent.Owner) ||
            _standing.IsDown(ent.Owner) ||
            !_safety.IsClassActionAllowed(ent.Owner, null, Wh40kClassEffectSafety.Mobility) ||
            !TryComp<PhysicsComponent>(ent, out var physics))
        return false;

        var start = _transform.GetMapCoordinates(ent.Owner);
        var requestedEnd = _transform.ToMapCoordinates(target);
        if (!TryGetSafeDashTarget(requestedEnd, out var safeTarget))
            return false;

        var end = _transform.ToMapCoordinates(safeTarget);
        if (start.MapId != end.MapId)
            return false;
        var delta = end.Position - start.Position;
        if (delta.LengthSquared() < 0.04f ||
            effect.Range > 0f && delta.LengthSquared() > effect.Range * effect.Range ||
            !HasSafeDashPath(start, end) ||
            !allowMarkedLowObstacles && !_interaction.InRangeUnobstructed(ent.Owner, safeTarget, effect.Range) ||
            allowMarkedLowObstacles && !HasAssaultJumpPath(ent.Owner, start, end))
        {
            return false;
        }

        var staminaCost = Math.Max(0f, effect.StaminaCost > 0f ? effect.StaminaCost : effect.Magnitude);
        if (staminaCost > 0 && !_stamina.TryTakeStamina(ent.Owner, staminaCost))
            return false;

        var duration = effect.Duration > TimeSpan.Zero
            ? TimeSpan.FromSeconds(Math.Clamp(effect.Duration.TotalSeconds, 0.15, 0.6))
            : TimeSpan.FromSeconds(0.3);
        var speed = Math.Clamp(delta.Length() / (float) duration.TotalSeconds, 3f, 12f);
        _physics.SetLinearVelocity(ent.Owner, Vector2.Normalize(delta) * speed, body: physics);
        EnsureComp<Wh40kClassDashRuntimeComponent>(ent).EndsAt = _timing.CurTime + duration;
        return StartRuntimeState(ent, effect, null, safeTarget);
    }

    private void StartBreachDash(Entity<Wh40kClassRuntimeProfileComponent> ent, Vector2 direction)
    {
        if (direction.LengthSquared() < 0.001f ||
            !IsLiving(ent.Owner) ||
            !_actionBlocker.CanMove(ent.Owner) ||
            _standing.IsDown(ent.Owner) ||
            !_safety.IsClassActionAllowed(ent.Owner, null, Wh40kClassEffectSafety.Mobility) ||
            !TryComp<PhysicsComponent>(ent, out var physics))
        {
            return;
        }

        var start = _transform.GetMapCoordinates(ent.Owner);
        var normalized = Vector2.Normalize(direction);
        var lastSafe = start;
        for (var distance = 0.2f; distance <= 4f; distance += 0.2f)
        {
            var candidate = new MapCoordinates(start.Position + normalized * distance, start.MapId);
            if (!TryGetSafeDashTarget(candidate, out var safe))
                break;
            lastSafe = _transform.ToMapCoordinates(safe);
        }

        var delta = lastSafe.Position - start.Position;
        if (delta.LengthSquared() < 0.01f)
            return;

        var duration = TimeSpan.FromSeconds(Math.Clamp(delta.Length() / 14f, 0.15f, 0.45f));
        _physics.SetLinearVelocity(ent.Owner, Vector2.Normalize(delta) * (delta.Length() / (float) duration.TotalSeconds), body: physics);
        EnsureComp<Wh40kClassDashRuntimeComponent>(ent).EndsAt = _timing.CurTime + duration;
    }

    private bool TryGetSafeDashTarget(MapCoordinates candidate, out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;
        if (candidate.MapId == MapId.Nullspace ||
            _safety.HasRule(candidate.MapId, candidate.Position, KoronusSafetyRule.ClassMobilityActions) ||
            !_mapManager.TryFindGridAt(candidate, out var gridUid, out var grid))
        {
            return false;
        }

        var tileIndices = _map.WorldToTile(gridUid, grid, candidate.Position);
        if (!_map.TryGetTileRef(gridUid, grid, tileIndices, out var tileRef) ||
            tileRef.Tile.IsEmpty ||
            _turf.IsSpace(tileRef) ||
            _turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
        {
            return false;
        }

        coordinates = _turf.GetTileCenter(tileRef);
        return true;
    }

    private bool HasSafeDashPath(MapCoordinates start, MapCoordinates end)
    {
        if (start.MapId != end.MapId)
            return false;

        var delta = end.Position - start.Position;
        var samples = Math.Max(1, (int) Math.Ceiling(delta.Length() / 0.35f));
        for (var index = 1; index <= samples; index++)
        {
            var candidate = new MapCoordinates(
                start.Position + delta * (index / (float) samples),
                start.MapId);
            if (!TryGetSafeDashTarget(candidate, out _))
                return false;
        }

        return true;
    }

    private bool HasAssaultJumpPath(EntityUid user, MapCoordinates start, MapCoordinates end)
    {
        var delta = end.Position - start.Position;
        var distance = delta.Length();
        if (distance <= 0.01f)
            return false;

        var worldStart = _transform.GetWorldPosition(user);
        var ray = new CollisionRay(
            worldStart,
            Vector2.Normalize(delta),
            (int) (CollisionGroup.Impassable | CollisionGroup.MidImpassable | CollisionGroup.LowImpassable |
                   CollisionGroup.InteractImpassable));
        foreach (var hit in _physics.IntersectRay(Transform(user).MapID, ray, distance, user, false))
        {
            if (hit.HitEntity != user && !HasComp<Wh40kClassLowDashObstacleComponent>(hit.HitEntity))
                return false;
        }

        return true;
    }

    private bool ApplyTrackedNpcPressure(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        EntityUid target,
        Wh40kResolvedClassEffect effect)
    {
        return ApplyNpcPressure(ent.Owner, target, effect) && StartRuntimeState(ent, effect, target);
    }

    private bool StartCloak(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        Wh40kResolvedClassEffect effect)
    {
        if (!StartRuntimeState(ent, effect, null, Transform(ent).Coordinates))
            return false;

        if (!TryComp<Wh40kClassCloakRuntimeComponent>(ent, out var runtime))
        {
            runtime = EnsureComp<Wh40kClassCloakRuntimeComponent>(ent);
            runtime.AddedStealth = !TryComp<StealthComponent>(ent, out var original);
            runtime.OriginalEnabled = original?.Enabled ?? false;
            runtime.OriginalVisibility = original == null ? 1f : _stealth.GetVisibility(ent, original);
            runtime.AddedStealthOnMove = !HasComp<StealthOnMoveComponent>(ent);
        }

        var stealth = EnsureComp<StealthComponent>(ent);
        _stealth.SetEnabled(ent, true, stealth);
        _stealth.SetVisibility(ent, Math.Clamp(-effect.Magnitude / 100f, -1.25f, -0.25f), stealth);
        EnsureComp<StealthOnMoveComponent>(ent);
        return true;
    }

    private void EndCloak(EntityUid body)
    {
        if (!TryComp<Wh40kClassCloakRuntimeComponent>(body, out var runtime))
            return;

        if (runtime.AddedStealthOnMove)
            RemComp<StealthOnMoveComponent>(body);
        if (runtime.AddedStealth)
        {
            RemComp<StealthComponent>(body);
        }
        else if (TryComp<StealthComponent>(body, out var stealth))
        {
            _stealth.SetEnabled(body, runtime.OriginalEnabled, stealth);
            _stealth.SetVisibility(body, runtime.OriginalVisibility, stealth);
        }

        RemComp<Wh40kClassCloakRuntimeComponent>(body);
    }

    private bool ApplyNpcPressure(
        EntityUid source,
        EntityUid target,
        Wh40kResolvedClassEffect effect)
    {
        if (!HasComp<ActiveNPCComponent>(target) ||
            _safety.IsPlayerCharacter(target) ||
            effect.Duration <= TimeSpan.Zero ||
            !_safety.IsClassActionAllowed(source, target, Wh40kClassEffectSafety.NpcOnly))
        {
            return false;
        }

        var magnitude = Math.Clamp(Math.Abs(effect.Magnitude), 0, 80);
        if (TryComp<Wh40kClassRuntimeProfileComponent>(source, out var profile))
        {
            magnitude = Math.Clamp(magnitude + profile.ActiveEffects.Values
                .Where(active => active.Mechanic == Wh40kClassRuntimeMechanic.PressureAmplifier)
                .Select(active => Math.Abs(active.Magnitude))
                .DefaultIfEmpty()
                .Max(), 0, 80);
        }

        var key = new Wh40kClassNpcPressureSource(source, effect.EffectId);
        var pressure = EnsureComp<Wh40kClassNpcPressureComponent>(target);
        var sameCategory = pressure.Sources
            .Where(pair => pair.Value.Category == effect.ModifierCategory)
            .ToArray();
        var winner = sameCategory
            .Append(new KeyValuePair<Wh40kClassNpcPressureSource, Wh40kClassNpcPressureState>(
                key,
                new Wh40kClassNpcPressureState(
                    _timing.CurTime + effect.Duration,
                    magnitude,
                    false,
                    effect.ModifierCategory)))
            .OrderByDescending(pair => pair.Value.Magnitude)
            .ThenBy(pair => pair.Key.EffectId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Source.ToString(), StringComparer.Ordinal)
            .First();
        if (winner.Key != key)
            return false;

        foreach (var existing in sameCategory)
            RemoveNpcPressureSource((target, pressure), existing.Key);

        var alreadyHostile = _npcFaction.IsForcedHostile(target, source);
        if (!alreadyHostile)
            _npcFaction.AggroEntity(target, source);
        pressure.Sources[key] = new Wh40kClassNpcPressureState(
            _timing.CurTime + effect.Duration,
            magnitude,
            !alreadyHostile,
            effect.ModifierCategory);
        _movement.RefreshMovementSpeedModifiers(target);
        Wh40kClassMetrics.ObserveNpcPressure(effect.ModifierCategory);
        return true;
    }

    internal bool ApplyNpcPressureForTest(
        EntityUid source,
        EntityUid target,
        Wh40kResolvedClassEffect effect)
    {
        return ApplyNpcPressure(source, target, effect);
    }

    private int ApplyAreaPressure(
        EntityUid source,
        EntityCoordinates origin,
        Wh40kResolvedClassEffect effect)
    {
        if (effect.Range <= 0f || effect.MaximumTargets <= 0)
            return 0;

        var map = _transform.ToMapCoordinates(origin);
        var count = 0;
        foreach (var npc in EntityManager.System<EntityLookupSystem>()
                     .GetEntitiesInRange<ActiveNPCComponent>(map, effect.Range)
                     .OrderBy(entity => (_transform.GetMapCoordinates(entity).Position - map.Position).LengthSquared()))
        {
            if (count >= effect.MaximumTargets ||
                !_interaction.InRangeUnobstructed(source, npc.Owner, effect.Range) ||
                !_safety.IsClassActionAllowed(source, npc.Owner, effect.Safety))
            {
                continue;
            }

            if (ApplyNpcPressure(source, npc.Owner, effect))
                count++;
        }

        return count;
    }

    private void RemoveNpcPressureSource(
        Entity<Wh40kClassNpcPressureComponent> ent,
        Wh40kClassNpcPressureSource source)
    {
        if (!ent.Comp.Sources.Remove(source, out var state) || !state.AddedHostility)
            return;

        var replacement = ent.Comp.Sources.FirstOrDefault(pair => pair.Key.Source == source.Source);
        if (replacement.Key != default)
        {
            ent.Comp.Sources[replacement.Key] = replacement.Value with { AddedHostility = true };
            return;
        }

        _npcFaction.DeAggroEntity(ent.Owner, source.Source);
    }

    private void BreakStates(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        Wh40kClassCounterplay flag)
    {
        foreach (var effectId in ent.Comp.RuntimeStates.Keys
                     .Where(effectId => ent.Comp.ActiveEffects.TryGetValue(effectId, out var effect) &&
                                        effect.Counterplay.HasFlag(flag))
                     .ToArray())
        {
            RemoveRuntimeState(ent, effectId);
        }
    }

    private void RemoveRuntimeState(
        Entity<Wh40kClassRuntimeProfileComponent> ent,
        string effectId)
    {
        if (!ent.Comp.RuntimeStates.Remove(effectId, out var state))
            return;

        if (state.Target is { } markedTarget &&
            ent.Comp.ActiveEffects.TryGetValue(effectId, out var markEffect) &&
            markEffect.Mechanic == Wh40kClassRuntimeMechanic.TargetMark)
        {
            SendTargetMarkVisual(ent.Comp, markedTarget, TimeSpan.Zero, true);
        }

        if (state.Target is { } target && TryComp<Wh40kClassInterceptTargetComponent>(target, out var intercept))
        {
            intercept.Sources.Remove(new Wh40kClassInterceptSource(ent.Owner, effectId));
            if (intercept.Sources.Count == 0)
                RemCompDeferred<Wh40kClassInterceptTargetComponent>(target);
        }

        if (TryComp<Wh40kClassCloakRuntimeComponent>(ent, out _) &&
            !ent.Comp.RuntimeStates.Keys.Any(id => ent.Comp.ActiveEffects.TryGetValue(id, out var effect) &&
                                                   effect.Mechanic == Wh40kClassRuntimeMechanic.Cloak))
        {
            EndCloak(ent.Owner);
        }
    }

    private static int GetCappedMagnitude(
        Wh40kClassRuntimeProfileComponent profile,
        Wh40kClassRuntimeMechanic mechanic,
        EntityUid? supportingItem,
        Func<Wh40kResolvedClassEffect, bool>? predicate = null)
    {
        return Math.Clamp(profile.ActiveEffects.Values
            .Where(effect => effect.Mechanic == mechanic &&
                             (predicate == null || predicate(effect)) &&
                             (!effect.RequiresEquipment || supportingItem == null || effect.SupportingItem == supportingItem))
            .Sum(effect => effect.Magnitude), -50, 75);
    }

    private void SendTargetMarkVisual(
        Wh40kClassRuntimeProfileComponent profile,
        EntityUid target,
        TimeSpan duration,
        bool clear)
    {
        if (!_players.TryGetSessionById(profile.UserId, out var session))
            return;

        RaiseNetworkEvent(
            new Wh40kClassTargetMarkVisualEvent(GetNetEntity(target), Math.Max(0f, (float) duration.TotalSeconds), clear),
            session);
    }

    private void CommitAction(
        Wh40kClassRuntimeProfileComponent profile,
        Wh40kResolvedClassEffect effect)
    {
        if (effect.Cooldown > TimeSpan.Zero)
            profile.CooldownEnds[effect.EffectId] = _timing.CurTime + effect.Cooldown;
    }

    private bool IsLiving(EntityUid uid)
    {
        return !TryComp<MobStateComponent>(uid, out var mob) || mob.CurrentState == MobState.Alive;
    }
}
