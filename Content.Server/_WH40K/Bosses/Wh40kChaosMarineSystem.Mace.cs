using System.Linq;
using System.Numerics;
using Content.Shared._WH40K.Bosses;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem
{
    [Dependency] private SharedJointSystem _joints = default!;

    private void InitializeMace()
    {
        SubscribeLocalEvent<Wh40kMarineMacePartComponent, PreventCollideEvent>(OnMacePreventCollide);
        SubscribeLocalEvent<Wh40kMarineMacePartComponent, StartCollideEvent>(OnMaceContact,
            before: [typeof(Content.Server.Projectiles.ProjectileSystem)]);
    }

    private void CreateMace(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        DestroyMace(uid, brain, state);
        var facing = _transform.GetWorldRotation(uid).ToWorldVec();
        var hand = _transform.GetWorldPosition(uid) + Wh40kChaosMarineWeaponAnimation.Grip(facing);
        var resting = _transform.GetWorldPosition(uid) + Wh40kChaosMarineWeaponAnimation.Rest(facing);
        var grip = Spawn("Wh40kMarineMaceGrip", new MapCoordinates(hand, Transform(uid).MapID));
        state.MaceGrip = grip;
        Comp<Wh40kMarineMacePartComponent>(grip).Boss = uid;
        Dirty(grip, Comp<Wh40kMarineMacePartComponent>(grip));
        var head = Spawn("Wh40kMarineMaceHead", _transform.GetMapCoordinates(uid).Offset(resting - _transform.GetWorldPosition(uid)));
        state.Mace = head;
        Comp<Wh40kMarineMacePartComponent>(head).Boss = uid;
        Dirty(head, Comp<Wh40kMarineMacePartComponent>(head));
        var length = Wh40kChaosMarineMacePhysics.ChainLength(state.Attack, state.Origin, state.Aim);
        var segment = length / (Wh40kChaosMarineMacePhysics.LinkCount + 1);
        var links = new EntityUid[Wh40kChaosMarineMacePhysics.LinkCount];
        var previous = grip;
        for (var i = 0; i <= links.Length; i++)
        {
            var part = head;
            if (i < links.Length)
            {
                var point = Vector2.Lerp(hand, resting, (i + 1f) / (links.Length + 1));
                part = Spawn("Wh40kMarineMaceLink", new MapCoordinates(point, Transform(uid).MapID));
                links[i] = part;
                Comp<Wh40kMarineMacePartComponent>(part).Boss = uid;
                Dirty(part, Comp<Wh40kMarineMacePartComponent>(part));
            }
            var joint = _joints.CreateDistanceJoint(previous, part, Vector2.Zero,
                i == links.Length ? new Vector2(0, -.2f) : Vector2.Zero, id: $"marine-mace-{state.Sequence}-{i}");
            joint.MinLength = .01f;
            joint.MaxLength = segment;
            joint.Length = segment;
            joint.CollideConnected = false;
            brain.MaceJoints.Add(joint);
            previous = part;
        }
        // A direct rope prevents solver stretch accumulating across eleven lightweight links.
        var tether = _joints.CreateDistanceJoint(grip, head, Vector2.Zero, new Vector2(0, -.2f),
            id: $"marine-mace-limit-{state.Sequence}");
        tether.MinLength = .01f;
        tether.MaxLength = tether.Length = length;
        tether.CollideConnected = false;
        brain.MaceJoints.Add(tether);
        brain.MacePreviousGoal = resting;
        var headMass = Comp<PhysicsComponent>(head).Mass;
        brain.MaceInertiaScale = 1;
        for (var i = 0; i < links.Length; i++)
        {
            var ratio = (i + 1f) / (links.Length + 1);
            brain.MaceInertiaScale += Comp<PhysicsComponent>(links[i]).Mass * ratio * ratio / MathF.Max(.01f, headMass);
        }
        brain.MaceLength = length;
        brain.MaceStrike = -1;
        state.MaceLinks = links.ToList();
        Dirty(uid, state);
    }

    private void DestroyMace(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        foreach (var joint in brain.MaceJoints) _joints.RemoveJoint(joint);
        brain.MaceJoints.Clear();
        if (state.Mace is { } head) QueueDel(head);
        if (state.MaceGrip is { } grip) QueueDel(grip);
        foreach (var link in state.MaceLinks) QueueDel(link);
        state.Mace = null;
        state.MaceGrip = null;
        state.MaceLinks = [];
        Dirty(uid, state);
    }

    private void UpdateMace(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state, float frameTime)
    {
        if (state.Mace is not { } head) return;
        if (state.MaceGrip is not { } grip || TerminatingOrDeleted(grip) ||
            TerminatingOrDeleted(head) || state.MaceLinks.Any(link => TerminatingOrDeleted(link)) ||
            Transform(head).MapID != Transform(uid).MapID)
        {
            DestroyMace(uid, brain, state);
            if (Wh40kChaosMarineCombat.IsMelee(state.Attack)) Recover(uid, brain, state, interrupted: true);
            return;
        }
        var active = Wh40kChaosMarineCombat.IsMelee(state.Attack);
        if (!active && (state.Attack != MarineAttack.None || Now >= state.EndsAt + TimeSpan.FromSeconds(state.WeaponReturnDuration)))
        {
            DestroyMace(uid, brain, state);
            return;
        }
        var facing = _transform.GetWorldRotation(uid).ToWorldVec();
        var body = _transform.GetWorldPosition(uid);
        var hand = body + Wh40kChaosMarineWeaponAnimation.Grip(facing);
        // Only the wrist is kinematic. Chain and head transforms are exclusively solver-owned.
        _transform.SetMapCoordinates(grip, new MapCoordinates(hand, Transform(uid).MapID));
        var goal = body + Wh40kChaosMarineWeaponAnimation.Rest(facing);
        var speed = Wh40kChaosMarineCombat.AttackSpeed(state.PhaseTwo, state.HeatStep);
        var pose = new MarineMacePose(Vector2.Zero, -1);
        if (active)
        {
            pose = MacePose(uid, brain, state);
            var delta = state.Aim - state.Origin;
            var direction = delta.LengthSquared() > .001f ? Vector2.Normalize(delta) : facing;
            var localGoal = Local(uid, uid) + direction * pose.Offset.X + new Vector2(-direction.Y, direction.X) * pose.Offset.Y;
            goal = _arena.World(state.Anchor!.Value, localGoal);
        }
        var wantedLength = Math.Clamp(Vector2.Distance(hand, goal) + .18f, .65f,
            active ? Wh40kChaosMarineCombat.Spec(state.Attack).Reach + .55f : 1.2f);
        if (active && state.Attack == MarineAttack.M4)
            wantedLength = pose.Offset.Length() + .55f;
        // Reeling alters the existing joints; no head/link transform is moved by an animation.
        brain.MaceLength += Math.Clamp(wantedLength - brain.MaceLength, -24 * speed * frameTime, 24 * speed * frameTime);
        for (var i = 0; i < brain.MaceJoints.Count; i++)
        {
            var length = i == brain.MaceJoints.Count - 1 ? brain.MaceLength
                : brain.MaceLength / (Wh40kChaosMarineMacePhysics.LinkCount + 1);
            brain.MaceJoints[i].MaxLength = length;
            brain.MaceJoints[i].Length = length;
        }
        var goalVelocity = active && Now >= state.ExecuteAt ? (goal - brain.MacePreviousGoal) / MathF.Max(frameTime, .001f) : Vector2.Zero;
        if (goalVelocity.Length() > 35 * speed) goalVelocity = Vector2.Normalize(goalVelocity) * (35 * speed);
        brain.MacePreviousGoal = goal;
        var physics = Comp<PhysicsComponent>(head);
        var acceleration = active && state.Attack == MarineAttack.M4 && Now >= state.ExecuteAt
            ? Wh40kChaosMarineMacePhysics.SpinAcceleration(_transform.GetWorldPosition(head) - body,
                _physics.GetMapLinearVelocity(head) - _physics.GetMapLinearVelocity(uid), pose.Offset.Length(), speed,
                brain.MaceInertiaScale, frameTime)
            : Wh40kChaosMarineMacePhysics.Acceleration(_transform.GetWorldPosition(head),
                _physics.GetMapLinearVelocity(head), goal, goalVelocity, speed, !active, frameTime);
        _physics.ApplyForce(head, acceleration * physics.Mass, body: physics);

        if (!active || Now < state.ExecuteAt) return;
        var strike = pose.Strike;
        if (strike != brain.MaceStrike)
        {
            brain.MaceStrike = strike;
            brain.Hit.Clear();
            if (strike >= 0) Sound(uid, "/Audio/Effects/thudswoosh.ogg", state.Attack == MarineAttack.M4 ? -10 : -4);
        }
        var contacts = _physics.GetContacts(head);
        while (contacts.MoveNext(out var contact))
            if (contact.IsTouching && contact.OtherFixture(head).Item2.Hard)
                TryMaceHit(uid, brain, state, contact.OtherEnt(head));
    }

    private void OnMacePreventCollide(EntityUid uid, Wh40kMarineMacePartComponent part, ref PreventCollideEvent args)
    {
        // Characters only touch the head's sensor; solid contacts are reserved for scenery.
        if (args.OurFixture.Hard && HasComp<MobStateComponent>(args.OtherEntity))
        {
            args.Cancelled = true;
            return;
        }
        if (part.Boss is not { } boss) return;
        if (args.OtherEntity == boss || TryComp<Wh40kMarineMacePartComponent>(args.OtherEntity, out var other) && other.Boss == boss ||
            _factions.IsEntityFriendly(boss, args.OtherEntity) || !part.Head && HasComp<MobStateComponent>(args.OtherEntity))
            args.Cancelled = true;
    }

    private void OnMaceContact(EntityUid uid, Wh40kMarineMacePartComponent part, ref StartCollideEvent args)
    {
        if (!part.Head || !args.OtherFixture.Hard || part.Boss is not { } boss || !TryComp<Wh40kChaosMarineBrainComponent>(boss, out var brain) ||
            !TryComp<Wh40kChaosMarineComponent>(boss, out var state) || state.Mace != uid) return;
        TryMaceHit(boss, brain, state, args.OtherEntity);
    }

    private void TryMaceHit(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state, EntityUid target)
    {
        if (brain.Suspended || !_npcs.Enabled || Paused(uid) || _mobs.IsDead(uid) ||
            !Wh40kChaosMarineCombat.IsMelee(state.Attack) || Now < state.ExecuteAt || Now > state.EndsAt ||
            !CanTarget(uid, target) ||
            state.Attack == MarineAttack.M1 && brain.ActionHit) return;
        var strike = MacePose(uid, brain, state).Strike;
        if (strike < 0) return;
        if (brain.MaceStrike != strike)
        {
            brain.MaceStrike = strike;
            brain.Hit.Clear();
        }
        if (brain.Hit.Contains(target)) return;
        // Only a target's hard body fixture counts, never an oversized sensor around it.
        if (state.Mace is not { } head || !ClearLine(uid, Local(uid, head), Local(uid, target), target)) return;
        // A spinning head being deployed/retrieved near the wrist is harmless.
        if (state.Attack == MarineAttack.M4 && Vector2.Distance(Local(uid, uid), Local(uid, head)) <
            1.2f) return;
        brain.Hit.Add(target);
        var finisher = state.Attack == MarineAttack.M6 && strike == 2;
        var damage = finisher ? 45 : Wh40kChaosMarineCombat.Spec(state.Attack).Damage;
        ApplyHit(uid, brain, target, damage, finisher ? 2 : 0, tool: head);
        if (state.Attack == MarineAttack.M5) brain.MeleeNextAt = Now;
    }
}
