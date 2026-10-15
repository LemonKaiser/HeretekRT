using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._WH40K.NPC;

public sealed partial class Wh40kChaosTacticsSystem
{
    private void InitializeSquad()
    {
        SubscribeLocalEvent<Wh40kChaosTacticsComponent, MobStateChangedEvent>(OnSquadMobState);
        SubscribeLocalEvent<Wh40kChaosTacticsComponent, ComponentShutdown>(OnTacticsShutdown);
    }

    public void ClearAssignment(EntityUid uid, Wh40kChaosTacticsComponent comp)
    {
        if (comp.AssignedTarget is { } target && TryComp<Wh40kChaosAttackReservationsComponent>(target, out var reservation))
            reservation.Attackers.Remove(uid);
        comp.AssignedTarget = null;
    }

    public void AssignTarget(EntityUid uid, EntityUid target, Wh40kChaosTacticsComponent comp)
    {
        if (comp.AssignedTarget != target)
        {
            ClearAssignment(uid, comp);
            comp.AssignedTarget = target;
            comp.TargetCommittedUntil = _timing.CurTime + TimeSpan.FromSeconds(3);
        }
        var reservation = EnsureComp<Wh40kChaosAttackReservationsComponent>(target);
        // .NET dictionary enumeration permits removal; expired claims cannot grow the table indefinitely.
        foreach (var (attacker, until) in reservation.Attackers)
        {
            if (until <= _timing.CurTime || Deleted(attacker))
                reservation.Attackers.Remove(attacker);
        }
        if (reservation.Attackers.Count < 16 || reservation.Attackers.ContainsKey(uid))
            reservation.Attackers[uid] = _timing.CurTime + TimeSpan.FromSeconds(2);
    }

    public int GetTargetPressure(EntityUid uid, EntityUid target)
    {
        if (!TryComp<Wh40kChaosAttackReservationsComponent>(target, out var reservation))
            return 0;
        var origin = _transform.GetMapCoordinates(uid);
        var count = 0;
        foreach (var (attacker, until) in reservation.Attackers)
        {
            if (attacker == uid || until <= _timing.CurTime || Deleted(attacker) ||
                !TryComp<MobStateComponent>(attacker, out var state) || state.CurrentState != MobState.Alive ||
                !_factions.IsEntityFriendly(uid, attacker) ||
                !origin.InRange(_transform.GetMapCoordinates(attacker), 8f))
                continue;
            count++;
        }
        return count;
    }

    private void OnTacticsShutdown(EntityUid uid, Wh40kChaosTacticsComponent comp, ComponentShutdown args)
    {
        ClearAssignment(uid, comp);
        CancelCover(comp);
        CancelThrowWarning(uid, true);
    }

    private void OnSquadMobState(EntityUid uid, Wh40kChaosTacticsComponent comp, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
        {
            ClearAssignment(uid, comp);
            CancelCover(comp);
            CancelThrowWarning(uid, true);
        }
        if (args.NewMobState != MobState.Dead || args.OldMobState == MobState.Dead || Paused(uid))
            return;

        var origin = _transform.GetMapCoordinates(uid);
        var notified = 0;
        var friends = _factionSetPool.Get();
        try
        {
            _lookup.GetEntitiesInRange(origin, 8f, friends);
            foreach (var (friend, faction) in friends)
            {
                if (friend == uid || Paused(friend) || !_factions.IsEntityFriendly(uid, (friend, faction)) ||
                    !HasComp<GunComponent>(friend) || HasComp<Wh40kChaosThrowerComponent>(friend) ||
                    !TryComp<Wh40kChaosTacticsComponent>(friend, out var tactics) ||
                    !TryComp<MobStateComponent>(friend, out var state) || state.CurrentState != MobState.Alive ||
                    !_interaction.InRangeUnobstructed(friend, uid, 8f, Content.Shared.Physics.CollisionGroup.Opaque,
                        e => _factions.IsEntityFriendly(friend, e)))
                    continue;
                if (++notified > 16)
                    break;
                if (_timing.CurTime >= tactics.LossWindowUntil)
                {
                    tactics.RecentLosses = 0;
                    tactics.LastLostAlly = null;
                    tactics.LossWindowUntil = _timing.CurTime + TimeSpan.FromSeconds(6);
                }
                if (tactics.LastLostAlly == uid)
                    continue;
                tactics.LastLostAlly = uid;
                if (++tactics.RecentLosses < 2 || _timing.CurTime < tactics.NextRegroup)
                    continue;
                tactics.RecentLosses = 0;
                tactics.RegroupUntil = _timing.CurTime + TimeSpan.FromSeconds(3);
                tactics.NextRegroup = _timing.CurTime + TimeSpan.FromSeconds(10);
                var xform = Transform(friend);
                tactics.RegroupOrigin = _transform.WithEntityId(xform.Coordinates, xform.GridUid ?? xform.MapUid!.Value);
                tactics.CoverPeeking = false;
                tactics.CoverPhaseUntil = TimeSpan.Zero;
                tactics.NextCoverSearch = TimeSpan.Zero;
            }
        }
        finally
        {
            _factionSetPool.Return(friends);
        }
    }
}
