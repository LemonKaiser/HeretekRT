using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server._WH40K.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._WH40K.Bosses;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.Player;
using Robust.Server.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private Wh40kBossArenaSystem _arena = default!;
    [Dependency] private Wh40kBossSystem _bosses = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private NPCSystem _npcs = default!;
    [Dependency] private Wh40kChaosTacticsSystem _chaosTactics = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private FlammableSystem _fire = default!;
    [Dependency] private GunSystem _guns = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private TimeSpan Now => _timing.CurTime;

    public override void Initialize()
    {
        SubscribeLocalEvent<Wh40kChaosMarineBrainComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<Wh40kChaosMarineBrainComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Wh40kBossArenaComponent, EntityUnpausedEvent>(OnUnpaused);
        SubscribeLocalEvent<Wh40kChaosMarineComponent, DamageModifyEvent>(OnResist);
        SubscribeLocalEvent<Wh40kChaosMarineComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
        SubscribeLocalEvent<Wh40kChaosMarineBrainComponent, DamageChangedEvent>(OnDamage);
        SubscribeLocalEvent<Wh40kChaosMarineBrainComponent, MobStateChangedEvent>(OnState);
        SubscribeLocalEvent<Wh40kChaosMarineComponent, GetFireProtectionEvent>(OnFire);
        SubscribeLocalEvent<Wh40kChaosMarineComponent, TryIgniteEvent>(OnIgnite);
        SubscribeLocalEvent<Wh40kChaosMarineComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
        SubscribeLocalEvent<Wh40kMarineMinionComponent, BeforeDamageChangedEvent>(OnMinionDamage);
        InitializeShots();
        InitializeMace();
        InitializeVoice();
        UpdatesAfter.Add(typeof(Content.Server._WH40K.Combat.PhantomStep.WH40KPhantomStepSystem));
        UpdatesBefore.Add(typeof(NPCSteeringSystem));
        UpdatesBefore.Add(typeof(NPCCombatSystem));
    }

    private void OnInit(EntityUid uid, Wh40kChaosMarineBrainComponent brain, MapInitEvent args)
    {
        if (!TryComp<Wh40kChaosMarineComponent>(uid, out var state) ||
            !TryComp<Wh40kChaosTacticsComponent>(uid, out var tactics))
            return;

        var xform = Transform(uid);
        var arena = EnsureComp<Wh40kBossArenaComponent>(uid);
        arena.Anchor = xform.GridUid ?? xform.MapUid;
        if (arena.Anchor is not { } anchor)
            return;
        arena.Center = _arena.Position(anchor, uid);
        Dirty(uid, arena);
        state.Anchor = anchor;
        state.Origin = state.Aim = arena.Center;
        Dirty(uid, state);
        // This director owns combat; native steering needs ActiveNPC, not a competing HTN plan.
        RemComp<HTNComponent>(uid);
        EnsureComp<ActiveNPCComponent>(uid);
        tactics.DodgeShotThreshold = 4;
        tactics.DodgeShotWindow = TimeSpan.FromSeconds(1.5);
        tactics.DodgeDuration = TimeSpan.FromSeconds(.4);
        tactics.DodgeCooldown = TimeSpan.FromSeconds(5);
        brain.NextStance = Now + TimeSpan.FromSeconds(15);
        brain.NextJump = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.JumpInitialDelaySeconds);
        brain.NextRocket = Now + TimeSpan.FromSeconds(60);
        brain.NextSmoke = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.SmokeCooldownSeconds);
        ConfigureTest(uid, brain, state);
    }

    public bool CanTarget(EntityUid boss, EntityUid player) =>
        boss != player && !TerminatingOrDeleted(player) && !HasComp<GhostComponent>(player) &&
        HasComp<MobStateComponent>(player) && !_mobs.IsDead(player) &&
        !_factions.IsEntityFriendly(boss, player) && _arena.Contains(boss, player);

    public bool HasParticipants(EntityUid boss)
    {
        foreach (var session in _players.Sessions)
            if (session.AttachedEntity is { } player && CanTarget(boss, player))
                return true;
        return false;
    }

    private void FindTargets(EntityUid uid, Wh40kChaosMarineBrainComponent brain)
    {
        brain.Targets.Clear();
        var bestScore = float.MaxValue;
        EntityUid? best = null;
        void ConsiderTarget(EntityUid target)
        {
            if (!CanTarget(uid, target) || brain.Targets.Contains(target)) return;
            brain.Targets.Add(target);
            var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(target));
            var score = distance + (Visible(uid, target) ? 0 : 10000) + (_mobs.IsCritical(target) ? 100000 : 0);
            if (target == brain.Target) score *= .8f;
            if (score >= bestScore) return;
            bestScore = score;
            best = target;
        }
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } target || !CanTarget(uid, target))
                continue;
            ConsiderTarget(target);
            if (brain.Audience.Add(session)) _pvs.AddSessionOverride(uid, session);
        }
        if (TryComp<Wh40kChaosMarineTestComponent>(uid, out var test) && test.Attack != null)
        {
            var targets = EntityQueryEnumerator<Wh40kChaosMarineTestTargetComponent>();
            while (targets.MoveNext(out var target, out _)) ConsiderTarget(target);
        }
        brain.Target = best;
        foreach (var session in brain.Audience.ToArray())
        {
            if (session.AttachedEntity is { } target && brain.Targets.Contains(target)) continue;
            _pvs.RemoveSessionOverride(uid, session);
            brain.Audience.Remove(session);
        }
    }

    private IEnumerable<EntityUid> LiveTargets(EntityUid uid, Wh40kChaosMarineBrainComponent brain) =>
        brain.Targets.Where(target => CanTarget(uid, target));

    private bool Visible(EntityUid uid, EntityUid target) =>
        _interaction.InRangeUnobstructed(uid, target, 36, CollisionGroup.Opaque,
            e => e == uid || _factions.IsEntityFriendly(uid, e));

    private void OnResist(EntityUid uid, Wh40kChaosMarineComponent state, DamageModifyEvent args)
    {
        args.Damage = new DamageSpecifier(args.Damage);
        foreach (var (type, value) in args.Damage.DamageDict.ToArray())
        {
            if (value <= 0)
                continue;
            var factor = type switch
            {
                "Blunt" or "Slash" or "Piercing" => .2f,
                "Heat" => Wh40kChaosMarineCombat.HeatMultiplier(state.PhaseTwo, state.HeatStep),
                _ => 0,
            };
            args.Damage.DamageDict[type] = value * factor;
        }
    }

    private void OnBeforeDamage(EntityUid uid, Wh40kChaosMarineComponent state, ref BeforeDamageChangedEvent args)
    {
        if (state.Dying) { args.Cancelled = true; return; }
        if (!args.Damage.DamageDict.Values.Any(v => v > 0))
            return;
        if (state.TransitionProtected ||
            TryComp<Wh40kChaosMarineTestComponent>(uid, out var test) && test.Invulnerable || FriendlySource(uid, args.Origin) ||
            args.Origin is { } attacker && HasComp<ActorComponent>(attacker) && !_arena.Contains(uid, attacker))
            args.Cancelled = true;
    }

    private bool FriendlySource(EntityUid boss, EntityUid? source) => source is { } uid &&
        (uid == boss || TryComp<Wh40kMarineMinionComponent>(uid, out var minion) && minion.Boss == boss ||
         TryComp<Wh40kMarineShotComponent>(uid, out var shot) && shot.Boss == boss);

    private void OnMinionDamage(EntityUid uid, Wh40kMarineMinionComponent minion, ref BeforeDamageChangedEvent args)
    {
        if (minion.Boss is { } boss && FriendlySource(boss, args.Origin) &&
            args.Damage.DamageDict.Values.Any(v => v > 0))
            args.Cancelled = true;
    }

    private void OnFire(EntityUid uid, Wh40kChaosMarineComponent state, ref GetFireProtectionEvent args)
    {
        if (state.Dying) { args.IgnoreAtmosphere = true; args.Multiplier = 0; return; }
        args.IgnoreAtmosphere = state.PhaseTwo;
        if (!_npcs.Enabled || !TryComp<Wh40kChaosMarineBrainComponent>(uid, out var brain) ||
            Now >= brain.FireDamageUntil || !HasParticipants(uid))
            args.Multiplier = 0;
    }

    private void OnIgnite(EntityUid uid, Wh40kChaosMarineComponent state, ref TryIgniteEvent args)
    {
        if (state.Dying) return;
        if (TryComp<FlammableComponent>(uid, out var fire))
            _fire.SetFireStacks(uid, MathF.Min(fire.FireStacks,
                Wh40kChaosMarineCombat.FireStackLimit(state.PhaseTwo, state.HeatStep)), fire);
    }

    private void OnSpeed(EntityUid uid, Wh40kChaosMarineComponent state, RefreshMovementSpeedModifiersEvent args)
    {
        var blocked = MovementLocked(uid, state);
        args.ModifySpeed(blocked ? 0 : Wh40kChaosMarineCombat.AttackSpeed(state.PhaseTwo, state.HeatStep));
    }

    private void OnDamage(EntityUid uid, Wh40kChaosMarineBrainComponent brain, DamageChangedEvent args)
    {
        // Native melee/projectiles attribute the hit to the player; ordinary burning has no origin.
        if (args.DamageIncreased && args.Origin is { } attacker &&
            HasComp<ActorComponent>(attacker) && CanTarget(uid, attacker))
            brain.FireDamageUntil = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.FireHitWindowSeconds);
        CheckHealth(uid, brain);
    }

    private void CheckHealth(EntityUid uid, Wh40kChaosMarineBrainComponent brain)
    {
        if (TryComp<Wh40kChaosMarineTestComponent>(uid, out var test) && test.Attack != null) return;
        if (!_bosses.TryGetHealth(uid, out var health, out var maximum) || health <= 0)
            return;
        var state = Comp<Wh40kChaosMarineComponent>(uid);
        if (health <= maximum * .75f) brain.WavesQueued |= 1;
        if (health <= maximum * .5f) brain.WavesQueued |= 2;
        if (health <= maximum * .25f) brain.WavesQueued |= 4;
        if (state.PhaseTwo)
        {
            var step = Math.Max(state.HeatStep, Wh40kChaosMarineCombat.Overheat(health, maximum));
            if (step != state.HeatStep)
            {
                state.HeatStep = step;
                Dirty(uid, state);
                _speed.RefreshMovementSpeedModifiers(uid);
                KeepFire(uid, state);
            }
        }
        else if (_npcs.Enabled && !HasComp<ActorComponent>(uid) &&
                 !state.TransitionProtected && health <= maximum * .5f)
        {
            CancelAction(uid, brain, state);
            BeginAction(uid, brain, state, MarineAttack.Transition);
            state.TransitionProtected = true;
            brain.PendingWave = 4;
            brain.CloseSince.Clear();
            RequestVoice(uid, brain, 90);
            Dirty(uid, state);
        }
        if (_npcs.Enabled && !HasComp<ActorComponent>(uid) && !state.TransitionProtected &&
            PendingWaves(brain) != 0 && HasParticipants(uid))
        {
            FindTargets(uid, brain);
            state.Engaged = true;
            TryStartSummon(uid, brain, state);
        }
    }

    private void OnState(EntityUid uid, Wh40kChaosMarineBrainComponent brain, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;
        StartDeath(uid, brain, Comp<Wh40kChaosMarineComponent>(uid));
    }

    private void OnShutdown(EntityUid uid, Wh40kChaosMarineBrainComponent brain, ComponentShutdown args) => Cleanup(uid, brain);

    private void OnUnpaused(EntityUid uid, Wh40kBossArenaComponent arena, ref EntityUnpausedEvent args)
    {
        if (!TryComp<Wh40kChaosMarineBrainComponent>(uid, out var brain)) return;
        brain.CloseSince.Clear();
        for (var i = 0; i < brain.Summons.Count; i++)
            brain.Summons[i] = brain.Summons[i] with { At = brain.Summons[i].At + args.PausedTime };
    }

    private void Cleanup(EntityUid uid, Wh40kChaosMarineBrainComponent brain)
    {
        brain.FireDamageUntil = TimeSpan.Zero;
        brain.ShotRetryUntil = TimeSpan.Zero;
        if (TryComp<Wh40kChaosMarineComponent>(uid, out var state))
        {
            state.TransitionProtected = state.Engaged = false;
            state.Attack = MarineAttack.None;
            state.LastAttack = MarineAttack.None;
            state.Points = [];
            state.RocketTargets = [];
            state.ResolvedRockets = [];
            state.RecoverUntil = TimeSpan.Zero;
            state.Sequence++;
            Dirty(uid, state);
        }
        _steering.Unregister(uid);
        _phantomStep.CancelDash(uid);
        ClearOwned(uid, brain);
        foreach (var session in brain.Audience) _pvs.RemoveSessionOverride(uid, session);
        brain.Audience.Clear();
        // The final coordinate-bound voice is independent of the corpse's lifetime.
        if (brain.VoicePriority < 100) _audio.Stop(brain.Voice);
        brain.Voice = null;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<Wh40kChaosMarineBrainComponent, Wh40kChaosMarineComponent>();
        while (query.MoveNext(out var uid, out var brain, out var state))
        {
            if (Paused(uid)) continue;
            if (state.Dying) { UpdateDeath(uid, brain, state); continue; }
            if (_mobs.IsDead(uid) || state.Anchor == null)
                continue;
            if (TerminatingOrDeleted(state.Anchor.Value))
            {
                Cleanup(uid, brain);
                state.Anchor = null;
                Dirty(uid, state);
                continue;
            }
            if (!_npcs.Enabled || HasComp<ActorComponent>(uid))
            {
                _steering.Unregister(uid);
                if (!brain.Suspended)
                {
                    brain.Suspended = true;
                    Comp<Content.Server._WH40K.NPC.Wh40kChaosTacticsComponent>(uid).DirectedMovementLocked = true;
                    CancelAction(uid, brain, state);
                    CancelSummons(brain);
                    state.Engaged = false;
                    state.RecoverUntil = TimeSpan.Zero;
                    brain.CloseSince.Clear();
                    _speed.RefreshMovementSpeedModifiers(uid);
                    Dirty(uid, state);
                }
                continue;
            }
            if (brain.Suspended)
            {
                brain.Suspended = false;
                brain.NextThink = Now;
                brain.NextAttack = state.RecoverUntil = Now + TimeSpan.FromSeconds(.75);
                CheckHealth(uid, brain);
                _speed.RefreshMovementSpeedModifiers(uid);
                Dirty(uid, state);
            }
            if (Now >= brain.NextThink)
            {
                brain.NextThink = Now + TimeSpan.FromSeconds(brain.DetectionInterval);
                FindTargets(uid, brain);
                if (brain.Targets.Count > 0) CheckHealth(uid, brain);
                RefreshCombatCooldowns(brain, state);
                Think(uid, brain, state);
                KeepFire(uid, state);
            }
            UpdateMovement(uid, brain, state);
            UpdateAction(uid, brain, state, frameTime);
            UpdateMace(uid, brain, state, frameTime);
            UpdateSummons(uid, brain);
            UpdateVoice(uid, brain);
        }
        UpdateShots(frameTime);
        UpdateMinions();
    }

    private void RefreshCombatCooldowns(Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        var speed = Wh40kChaosMarineCombat.AttackSpeed(state.PhaseTwo, state.HeatStep);
        if (speed == brain.CooldownSpeed) return;
        var ratio = brain.CooldownSpeed / speed;
        TimeSpan Remaining(TimeSpan deadline) => deadline > Now
            ? Now + TimeSpan.FromTicks((long) ((deadline - Now).Ticks * ratio)) : deadline;
        brain.NextRocket = Remaining(brain.NextRocket);
        brain.NextJump = Remaining(brain.NextJump);
        brain.NextStance = Remaining(brain.NextStance);
        brain.NextPattern = Remaining(brain.NextPattern);
        brain.NextBasicRanged = Remaining(brain.NextBasicRanged);
        brain.NextBasicMelee = Remaining(brain.NextBasicMelee);
        brain.NextMovement = Remaining(brain.NextMovement);
        brain.NextOrbitChange = Remaining(brain.NextOrbitChange);
        brain.NextPatrol = Remaining(brain.NextPatrol);
        brain.CooldownSpeed = speed;
    }

    private void KeepFire(EntityUid uid, Wh40kChaosMarineComponent state)
    {
        if (!TryComp<FlammableComponent>(uid, out var fire))
            return;
        fire.MaximumFireStacks = Wh40kChaosMarineCombat.FireStackLimit(state.PhaseTwo, state.HeatStep);
        _fire.SetFireStacks(uid, Math.Clamp(fire.FireStacks, state.PhaseTwo ? fire.MaximumFireStacks : fire.MinimumFireStacks,
            fire.MaximumFireStacks), fire, ignite: state.PhaseTwo);
        if (state.PhaseTwo)
        {
            _fire.Ignite(uid, uid, fire);
        }
    }

    private void Pursue(EntityUid uid, EntityCoordinates coordinates, float range)
    {
        var steering = _steering.Register(uid, coordinates);
        steering.Flags = PathFlags.Climbing | PathFlags.Interact | PathFlags.Prying | PathFlags.Smashing;
        steering.Radius = .35f;
        steering.Range = range;
        steering.ArriveOnLineOfSight = false;
        steering.DirectMove = CanWalkDirect(uid, coordinates);
        if (steering.Status == SteeringStatus.NoPath)
        {
            steering.FailedPathCount = 0;
            steering.Status = SteeringStatus.Moving;
        }
    }

    private bool CanWalkDirect(EntityUid uid, EntityCoordinates destination) => _chaosTactics.CanMoveDirectly(uid, destination);

    private Vector2 Local(EntityUid uid, EntityUid target) => _arena.Position(Comp<Wh40kChaosMarineComponent>(uid).Anchor!.Value, target);
    private EntityCoordinates Coordinates(Wh40kChaosMarineComponent state, Vector2 point) => new(state.Anchor!.Value, point);

    private void Face(EntityUid uid, Wh40kChaosMarineComponent state)
    {
        var direction = state.Aim - Local(uid, uid);
        if (direction.LengthSquared() > .001f)
            _transform.SetLocalRotation(uid, direction.ToWorldAngle());
    }
}
