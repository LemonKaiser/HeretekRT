using System.Linq;
using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.Systems;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.Physics;

namespace Content.Server._WH40K.NPC;

public sealed partial class Wh40kChaosTacticsSystem
{
    [Dependency] private NPCUtilitySystem _utility = default!;
    [ViewVariables] public long TargetSelectionCount;

    private bool IsCombatTarget(EntityUid target)
    {
        if (TerminatingOrDeleted(target))
            return false;
        return TryComp<MobStateComponent>(target, out var state)
            ? state.CurrentState == MobState.Alive
            : HasComp<DamageableComponent>(target) && HasComp<NpcFactionMemberComponent>(target);
    }

    public bool CanFightTarget(EntityUid uid, EntityUid target, Wh40kChaosTacticsComponent comp)
    {
        if (TryComp<Content.Server._WH40K.Bosses.Wh40kMarineMinionComponent>(uid, out var minion) &&
            (_timing.CurTime < minion.ReadyAt || minion.Boss is not { } boss || TerminatingOrDeleted(boss) ||
             !EntityManager.System<Content.Server._WH40K.Bosses.Wh40kChaosMarineSystem>().CanTarget(boss, target)))
            return false;
        return target != uid && IsCombatTarget(target) &&
               Transform(target).MapID == Transform(uid).MapID && Transform(target).MapUid != null &&
               !_factions.IsEntityFriendly(uid, target) &&
               (!TryComp<FactionExceptionComponent>(uid, out var exceptions) ||
                !_factions.IsIgnored((uid, exceptions), target)) &&
               (comp.IgnoredTarget != target || _timing.CurTime >= comp.IgnoreUntil);
    }

    private bool CanSeeTarget(EntityUid uid, EntityUid target, Wh40kChaosTacticsComponent comp, float vision)
    {
        if (IsAlertedTo(comp, target))
            vision = MathF.Max(vision, AlertVisionRange);
        return _interaction.InRangeUnobstructed(uid, target, vision, CollisionGroup.Opaque,
            e => _factions.IsEntityFriendly(uid, e));
    }

    /// <summary>Selects visible enemies with native scoring, retaining a remembered target only when nobody is visible.</summary>
    public EntityUid SelectCombatTarget(NPCBlackboard blackboard, Wh40kChaosTacticsComponent comp,
        float meleeRange = 2f, string targetKey = "Target")
    {
        TargetSelectionCount++;
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var vision = blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(EntityManager), EntityManager);
        var origin = _transform.GetMapCoordinates(uid);
        blackboard.TryGetValue<EntityUid>(targetKey, out var current, EntityManager);
        var currentValid = current.IsValid() && CanFightTarget(uid, current, comp);
        var currentVisible = currentValid && CanSeeTarget(uid, current, comp, vision);
        var currentDistance = currentValid ? Vector2.Distance(origin.Position, _transform.GetMapCoordinates(current).Position) : float.MaxValue;

        var result = _utility.GetEntities(blackboard, "Wh40kChaosCombatTargets", bestOnly: false);
        var close = EntityUid.Invalid;
        var closeDistance = meleeRange;
        foreach (var candidate in result.Entities.Keys.ToArray())
        {
            // A previously unreachable enemy can step onto our side of the obstacle during the retry cooldown.
            if (comp.IgnoredTarget == candidate && origin.InRange(_transform.GetMapCoordinates(candidate), meleeRange) &&
                CanMoveDirectly(uid, candidate))
                comp.IgnoredTarget = null;
            if (!CanFightTarget(uid, candidate, comp) || !CanSeeTarget(uid, candidate, comp, vision))
            {
                result.Entities.Remove(candidate);
                continue;
            }
            result.Entities[candidate] /= 1f + GetTargetPressure(uid, candidate) * 0.6f;

            var distance = Vector2.Distance(origin.Position, _transform.GetMapCoordinates(candidate).Position);
            if (distance > closeDistance ||
                !_interaction.InRangeUnobstructed(uid, candidate, meleeRange,
                    CollisionGroup.Impassable | CollisionGroup.InteractImpassable))
                continue;
            close = candidate;
            closeDistance = distance;
        }

        // Keep an ongoing close fight stable, but never walk past a reachable enemy to chase a distant one.
        if (currentVisible && currentDistance <= meleeRange &&
            _interaction.InRangeUnobstructed(uid, current, meleeRange,
                CollisionGroup.Impassable | CollisionGroup.InteractImpassable))
            return current;
        if (close.IsValid())
            return close;

        var best = result.GetHighest();
        var bestDistance = best.IsValid()
            ? Vector2.Distance(origin.Position, _transform.GetMapCoordinates(best).Position)
            : float.MaxValue;
        var hasThreat = TryGetThreatTarget(uid, comp, out var threat);
        if (hasThreat && CanSeeTarget(uid, threat, comp, vision))
        {
            var threatDistance = Vector2.Distance(origin.Position, _transform.GetMapCoordinates(threat).Position);
            // Crossfire should change shelter direction without restarting aiming and cover on every incoming shot.
            if (currentVisible && current != threat && _timing.CurTime < comp.TargetCommittedUntil &&
                !(threatDistance + 1f < currentDistance && threatDistance < currentDistance * 0.7f) &&
                !(bestDistance + 1f < currentDistance && bestDistance < currentDistance * 0.7f))
                return current;
            // Retaliation is a preference, not permission to bypass a substantially closer enemy.
            return bestDistance + 1f < threatDistance && bestDistance < threatDistance * 0.5f ? best : threat;
        }

        if (!currentVisible)
            return best.IsValid() ? best : currentValid ? current : hasThreat ? threat : EntityUid.Invalid;
        if (!best.IsValid() || best == current)
            return current;

        // Require a meaningful distance advantage so small movements do not restart attacks and warnings.
        if (bestDistance + 1f < currentDistance && bestDistance < currentDistance * 0.7f)
            return best;
        if (_timing.CurTime >= comp.TargetCommittedUntil && bestDistance <= currentDistance + 2f &&
            result.Entities.TryGetValue(current, out var currentScore) && result.Entities[best] > currentScore * 1.35f &&
            GetTargetPressure(uid, current) >= GetTargetPressure(uid, best) + 2)
            return best;
        return current;
    }
}
