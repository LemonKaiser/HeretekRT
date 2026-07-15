using Content.Server.Ghost.Roles.Components;
using Content.Server.NPC.HTN;
using Content.Shared._Mono.CCVar;
using Content.Shared.Buckle.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Cleanup;

/// <summary>
///     Deletes mobs too far from players.
/// </summary>
public sealed partial class MobCleanupSystem : BaseCleanupSystem<HTNComponent>
{
    [Dependency] private CleanupHelperSystem _cleanup = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private IGameTiming _timing = default!;

    private float _maxDistance;
    private float _maxGridDistance;
    private float _corpseDistance;
    private TimeSpan _cleanupDuration;
    private TimeSpan _corpseCleanupDuration;

    private EntityQuery<ActorComponent> _actorQuery;
    private EntityQuery<BuckleComponent> _buckleQuery;
    private EntityQuery<GhostRoleComponent> _ghostQuery;
    private EntityQuery<CleanupImmuneComponent> _immuneQuery;
    private EntityQuery<CleanupPlayerProtectedComponent> _playerProtectedQuery;
    private EntityQuery<MindContainerComponent> _mindQuery;
    private EntityQuery<MobStateComponent> _mobStateQuery;
    private EntityQuery<PullableComponent> _pullableQuery;

    public override void Initialize()
    {
        base.Initialize();

        _actorQuery = GetEntityQuery<ActorComponent>();
        _buckleQuery = GetEntityQuery<BuckleComponent>();
        _ghostQuery = GetEntityQuery<GhostRoleComponent>();
        _immuneQuery = GetEntityQuery<CleanupImmuneComponent>();
        _playerProtectedQuery = GetEntityQuery<CleanupPlayerProtectedComponent>();
        _mindQuery = GetEntityQuery<MindContainerComponent>();
        _mobStateQuery = GetEntityQuery<MobStateComponent>();
        _pullableQuery = GetEntityQuery<PullableComponent>();

        SubscribeLocalEvent<ActorComponent, ComponentStartup>(OnActorStartup);
        SubscribeLocalEvent<MindContainerComponent, MindAddedMessage>(OnMindAdded);

        Subs.CVar(_cfg, MonoCVars.MobCleanupDistance, val => _maxDistance = val, true);
        Subs.CVar(_cfg, MonoCVars.CleanupMaxGridDistance, val => _maxGridDistance = val, true);
        Subs.CVar(_cfg, MonoCVars.MobCleanupDuration,
            val => _cleanupDuration = TimeSpan.FromSeconds(Math.Max(0f, val)), true);
        Subs.CVar(_cfg, MonoCVars.MobCorpseCleanupDuration,
            val => _corpseCleanupDuration = TimeSpan.FromSeconds(Math.Max(0f, val)), true);
        Subs.CVar(_cfg, MonoCVars.MobCorpseCleanupDistance,
            val => _corpseDistance = Math.Max(0f, val), true);
    }

    protected override bool ShouldEntityCleanup(EntityUid uid)
    {
        var xform = Transform(uid);

        if (xform.MapID == MapId.Nullspace ||
            _immuneQuery.HasComp(uid) ||
            _ghostQuery.HasComp(uid) ||
            _playerProtectedQuery.HasComp(uid) ||
            _actorQuery.HasComp(uid) ||
            _mindQuery.TryComp(uid, out var mind) && mind.HasMind ||
            _containers.IsEntityInContainer(uid) ||
            _pullableQuery.TryComp(uid, out var pullable) && pullable.BeingPulled ||
            _buckleQuery.TryComp(uid, out var buckle) && buckle.Buckled)
        {
            ResetEligibility(uid);
            return false;
        }

        var corpse = _mobStateQuery.TryComp(uid, out var mobState) && mobState.CurrentState == MobState.Dead;
        TimeSpan requiredDuration;

        if (xform.GridUid != null)
        {
            // Living NPCs on stations, planets and ships are gameplay content, not garbage.
            if (!corpse || _cleanup.HasNearbyPlayers(xform.Coordinates, _corpseDistance))
            {
                ResetEligibility(uid);
                return false;
            }

            requiredDuration = _corpseCleanupDuration;
        }
        else
        {
            if (_cleanup.HasNearbyPlayers(xform.Coordinates, _maxDistance) ||
                _cleanup.HasNearbyGrids(xform.Coordinates, _maxGridDistance))
            {
                ResetEligibility(uid);
                return false;
            }

            requiredDuration = corpse ? _corpseCleanupDuration : _cleanupDuration;
        }

        var state = EnsureComp<MobCleanupStateComponent>(uid);
        var now = _timing.CurTime;
        var elapsed = state.LastEvaluation == TimeSpan.Zero
            ? TimeSpan.Zero
            : now - state.LastEvaluation;
        state.LastEvaluation = now;

        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        else if (elapsed > _cleanupInterval * 2)
            elapsed = _cleanupInterval;

        state.EligibleFor += elapsed;
        return state.EligibleFor >= requiredDuration;
    }

    private void ResetEligibility(EntityUid uid)
    {
        if (HasComp<MobCleanupStateComponent>(uid))
            RemCompDeferred<MobCleanupStateComponent>(uid);
    }

    private void OnActorStartup(Entity<ActorComponent> entity, ref ComponentStartup args)
    {
        EnsureComp<CleanupPlayerProtectedComponent>(entity);
    }

    private void OnMindAdded(Entity<MindContainerComponent> entity, ref MindAddedMessage args)
    {
        EnsureComp<CleanupPlayerProtectedComponent>(entity);
    }
}
