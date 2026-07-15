using System.Numerics;
using Content.Server.Cargo.Systems;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.CCVar;
using Content.Shared.Buckle.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Cleanup;

/// <summary>
///     Periodically removes explicit, abandoned trash without treating every item as disposable.
/// </summary>
public sealed partial class GarbageCleanupSystem : BaseCleanupSystem<SpaceGarbageComponent>
{
    private static readonly ProtoId<TagPrototype> TrashTag = "Trash";
    private const float MovementToleranceSquared = 0.05f * 0.05f;
    private const float VelocityToleranceSquared = 0.01f * 0.01f;
    private const float AngularVelocityTolerance = 0.01f;

    [Dependency] private CleanupHelperSystem _cleanup = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private IGameTiming _timing = default!;

    private TimeSpan _duration;
    private TimeSpan _protectedGridDuration;
    private TimeSpan _spaceDuration;
    private float _playerDistance;
    private float _maxValue;

    private EntityQuery<ActorComponent> _actorQuery;
    private EntityQuery<BuckleComponent> _buckleQuery;
    private EntityQuery<CartridgeAmmoComponent> _cartridgeQuery;
    private EntityQuery<CleanupImmuneComponent> _immuneQuery;
    private EntityQuery<CleanupPlayerProtectedComponent> _playerProtectedQuery;
    private EntityQuery<ContainerManagerComponent> _containerManagerQuery;
    private EntityQuery<GhostRoleComponent> _ghostRoleQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<MindContainerComponent> _mindQuery;
    private EntityQuery<MobStateComponent> _mobStateQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<PullableComponent> _pullableQuery;

    public override void Initialize()
    {
        base.Initialize();

        _cleanupInterval = TimeSpan.FromMinutes(5);

        _actorQuery = GetEntityQuery<ActorComponent>();
        _buckleQuery = GetEntityQuery<BuckleComponent>();
        _cartridgeQuery = GetEntityQuery<CartridgeAmmoComponent>();
        _immuneQuery = GetEntityQuery<CleanupImmuneComponent>();
        _playerProtectedQuery = GetEntityQuery<CleanupPlayerProtectedComponent>();
        _containerManagerQuery = GetEntityQuery<ContainerManagerComponent>();
        _ghostRoleQuery = GetEntityQuery<GhostRoleComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _mindQuery = GetEntityQuery<MindContainerComponent>();
        _mobStateQuery = GetEntityQuery<MobStateComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _pullableQuery = GetEntityQuery<PullableComponent>();

        Subs.CVar(_cfg, MonoCVars.GarbageCleanupDuration,
            value => _duration = TimeSpan.FromSeconds(Math.Max(0f, value)), true);
        Subs.CVar(_cfg, MonoCVars.GarbageCleanupProtectedGridDuration,
            value => _protectedGridDuration = TimeSpan.FromSeconds(Math.Max(0f, value)), true);
        Subs.CVar(_cfg, MonoCVars.GarbageCleanupSpaceDuration,
            value => _spaceDuration = TimeSpan.FromSeconds(Math.Max(0f, value)), true);
        Subs.CVar(_cfg, MonoCVars.GarbageCleanupPlayerDistance,
            value => _playerDistance = Math.Max(0f, value), true);
        Subs.CVar(_cfg, MonoCVars.GarbageCleanupMaxValue,
            value => _maxValue = Math.Max(0f, value), true);
    }

    protected override bool ShouldEntityCleanup(EntityUid uid)
    {
        var now = _timing.CurTime;

        if (!TryComp<SpaceGarbageComponent>(uid, out var garbage) || garbage.CleanupExempt ||
            !_tags.HasTag(uid, TrashTag) ||
            _gridQuery.HasComp(uid) ||
            _immuneQuery.HasComp(uid) ||
            _playerProtectedQuery.HasComp(uid) ||
            _actorQuery.HasComp(uid) ||
            _mindQuery.HasComp(uid) ||
            _mobStateQuery.HasComp(uid) ||
            _ghostRoleQuery.HasComp(uid) ||
            _containers.IsEntityInContainer(uid) ||
            HasContainedEntities(uid) ||
            _cartridgeQuery.TryComp(uid, out var cartridge) && !cartridge.Spent)
        {
            RemoveExistingEligibility(uid);
            return false;
        }

        var state = EnsureComp<GarbageCleanupStateComponent>(uid);

        var xform = Transform(uid);
        if (xform.MapID == MapId.Nullspace || xform.Anchored ||
            _pullableQuery.TryComp(uid, out var pullable) && pullable.BeingPulled ||
            _buckleQuery.TryComp(uid, out var buckle) && buckle.Buckled ||
            _physicsQuery.TryComp(uid, out var physics) &&
            (physics.LinearVelocity.LengthSquared() > VelocityToleranceSquared ||
             MathF.Abs(physics.AngularVelocity) > AngularVelocityTolerance) ||
            _cleanup.HasNearbyPlayers(xform.Coordinates, _playerDistance) ||
            _pricing.GetPrice(uid) > _maxValue)
        {
            ResetEligibility(uid, state, now);
            return false;
        }

        var moved = state.HasPositionSample &&
                    (state.LastParent != xform.ParentUid ||
                     Vector2.DistanceSquared(state.LastLocalPosition, xform.LocalPosition) > MovementToleranceSquared);

        state.LastParent = xform.ParentUid;
        state.LastLocalPosition = xform.LocalPosition;
        state.HasPositionSample = true;

        if (moved)
        {
            state.EligibleFor = TimeSpan.Zero;
            state.LastEvaluation = now;
            state.EligibilityActive = false;
            return false;
        }

        if (!state.EligibilityActive)
        {
            state.EligibilityActive = true;
            state.LastEvaluation = now;
            return state.EligibleFor >= GetRequiredDuration(xform);
        }

        var elapsed = state.LastEvaluation == TimeSpan.Zero
            ? TimeSpan.Zero
            : now - state.LastEvaluation;
        state.LastEvaluation = now;

        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        else if (elapsed > _cleanupInterval * 2)
            elapsed = _cleanupInterval;

        state.EligibleFor += elapsed;

        return state.EligibleFor >= GetRequiredDuration(xform);
    }

    private TimeSpan GetRequiredDuration(TransformComponent xform)
    {
        return xform.GridUid is not { } grid
            ? _spaceDuration
            : _cleanup.IsGridProtectedFromCleanup(grid)
                ? _protectedGridDuration
                : _duration;
    }

    private bool HasContainedEntities(EntityUid uid)
    {
        if (!_containerManagerQuery.TryComp(uid, out var manager))
            return false;

        foreach (var container in manager.Containers.Values)
        {
            if (container.ContainedEntities.Count != 0)
                return true;
        }

        return false;
    }

    private void ResetEligibility(EntityUid uid, GarbageCleanupStateComponent state, TimeSpan now)
    {
        state.EligibleFor = TimeSpan.Zero;
        state.LastEvaluation = now;
        state.EligibilityActive = false;

        if (!TryComp(uid, out TransformComponent? xform))
        {
            state.HasPositionSample = false;
            return;
        }

        state.LastParent = xform.ParentUid;
        state.LastLocalPosition = xform.LocalPosition;
        state.HasPositionSample = true;
    }

    private void RemoveExistingEligibility(EntityUid uid)
    {
        if (HasComp<GarbageCleanupStateComponent>(uid))
            RemCompDeferred<GarbageCleanupStateComponent>(uid);
    }
}
