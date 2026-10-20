using System.Numerics;
using Content.Shared.NPC;
using Content.Shared._WH40K.Bosses;
using Content.Shared.Atmos.Components;
using Robust.Shared.Physics;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem
{
    private void StartDeath(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (state.Dying || brain.DeathFinished) return;
        Cleanup(uid, brain);
        _fire.Extinguish(uid);
        RequestVoice(uid, brain, 100);
        state.Dying = true;
        state.DeathBurnAt = brain.VoiceUntil > Now ? brain.VoiceUntil : Now + TimeSpan.FromSeconds(.25);
        state.DeathEndsAt = state.DeathBurnAt + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.DeathBurnSeconds);
        RemComp<ActiveNPCComponent>(uid);
        RemComp<Content.Shared.Weapons.Reflect.ReflectComponent>(uid);
        _physics.SetLinearVelocity(uid, Vector2.Zero);
        _physics.SetAngularVelocity(uid, 0);
        _physics.SetBodyType(uid, BodyType.Static);
        Dirty(uid, state);
        _speed.RefreshMovementSpeedModifiers(uid);
    }

    private void UpdateDeath(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (brain.DeathFinished || Now < state.DeathBurnAt) return;
        if (Now >= state.DeathEndsAt)
        {
            // Mark before spawning: repeated death events/updates must never duplicate the reward.
            brain.DeathFinished = true;
            var coordinates = Transform(uid).Coordinates;
            var loot = coordinates;
            if (state.Anchor is { } anchor && !TerminatingOrDeleted(anchor))
            {
                var origin = Local(uid, uid);
                var center = new Vector2(MathF.Floor(origin.X) + .5f, MathF.Floor(origin.Y) + .5f);
                Vector2[] offsets = [new(2, 0), new(-2, 0), new(0, 2), new(0, -2), new(1, 1), new(-1, -1)];
                foreach (var offset in offsets)
                    if (SpawnPoint(uid, state, center + offset) && ClearLine(uid, origin, center + offset))
                    {
                        loot = Coordinates(state, center + offset);
                        break;
                    }
            }
            Spawn("Ash", coordinates);
            Spawn("CrateWh40kChaosMarineSpoils", loot);
            QueueDel(uid);
            return;
        }
        if (!TryComp<FlammableComponent>(uid, out var fire)) return;
        fire.MaximumFireStacks = fire.MinimumFireStacks = 100;
        fire.FirestackFade = 0;
        fire.CanExtinguish = false;
        if (fire.FireStacks != 100 || !fire.OnFire)
        {
            _fire.SetFireStacks(uid, 100, fire);
            _fire.Ignite(uid, uid, fire);
        }
    }
}
