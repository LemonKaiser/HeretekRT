using System.Linq;
using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WH40K.Bosses;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem
{
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedMapSystem _maps = default!;

    private static readonly string[] MinionTypes =
    [
        "MobWH40KChaosBruiser", "MobWH40KChaosShooter", "MobWH40KChaosFlamer", "MobWH40KChaosStalker",
        "MobWH40KChaosShotgunner", "MobWH40KChaosGunner", "MobWH40KChaosBomber", "MobWH40KChaosRager",
    ];

    private void PlaceSummons(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (brain.SummonPlaced) return;
        brain.SummonPlaced = true;
        if (!state.Engaged) return;
        var wave = brain.PendingWave / 2;
        if ((brain.WavesDone & wave) != 0) return;
        brain.SpawnedMinions.TryGetValue(wave, out var spawned);
        var center = Local(uid, uid);
        // Appears after the two-second preparation; disappears at five seconds into the summon.
        var circle = Effect(uid, state, center, MarineEffect.Summon, 2, 3);
        for (var i = 0; i < brain.PendingWave; i++)
        {
            if ((spawned & (1 << i)) != 0 || brain.Summons.Any(s => s.Wave == wave && s.TypeIndex == i)) continue;
            brain.Summons.Add(new MarineSpawn(circle, MinionTypes[i], Coordinates(state, center), Now + TimeSpan.FromSeconds(1.5), wave, i));
        }
        brain.NextSummonRetry = Now + TimeSpan.FromSeconds(2);
        Sound(uid, "/Audio/Effects/teleport_arrival.ogg", -4);
    }

    private IEnumerable<Vector2> SummonCandidates(EntityUid uid, Wh40kChaosMarineBrainComponent brain,
        Wh40kChaosMarineComponent state, Vector2 center)
    {
        for (var x = (int) MathF.Floor(center.X - 2); x <= center.X + 2; x++)
        for (var y = (int) MathF.Floor(center.Y - 2); y <= center.Y + 2; y++)
        {
            var point = new Vector2(x + .5f, y + .5f);
            if (Vector2.DistanceSquared(center, point) <= 4 && SpawnPoint(uid, state, point) &&
                ClearLine(uid, center, point) && Vector2.Distance(Local(uid, uid), point) >= .9f &&
                LiveTargets(uid, brain).All(t => Vector2.Distance(Local(uid, t), point) >= 1.25f)) yield return point;
        }
    }

    private static int PendingWaves(Wh40kChaosMarineBrainComponent brain)
    {
        var pending = brain.WavesQueued & ~brain.WavesDone;
        foreach (var summon in brain.Summons) pending &= ~summon.Wave;
        return pending;
    }

    private bool TryStartSummon(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (!state.Engaged || state.Attack != MarineAttack.None || Now < state.RecoverUntil ||
            Now < brain.NextAttack || Now < brain.NextSummonRetry)
            return false;
        var pending = PendingWaves(brain);
        if (pending == 0) return false;
        var bit = (pending & 1) != 0 ? 1 : (pending & 2) != 0 ? 2 : 4;
        CancelAction(uid, brain, state);
        state.RecoverUntil = TimeSpan.Zero;
        brain.PendingWave = bit * 2;
        BeginAction(uid, brain, state, MarineAttack.Summon);
        RequestVoice(uid, brain, 70);
        return true;
    }

    private bool SpawnPoint(EntityUid uid, Wh40kChaosMarineComponent state, Vector2 point)
    {
        if (!ValidPoint(uid, state, point, .6f) || state.Anchor is not { } anchor ||
            !TryComp<MapGridComponent>(anchor, out var grid) ||
            !_maps.TryGetTileRef(anchor, grid, new EntityCoordinates(anchor, point), out var tile))
            return false;
        return !_turf.IsSpace(tile) && !_turf.IsTileBlocked(tile, CollisionGroup.MobMask);
    }

    private void UpdateSummons(EntityUid uid, Wh40kChaosMarineBrainComponent brain)
    {
        if (brain.Summons.Count == 0) return;
        var state = Comp<Wh40kChaosMarineComponent>(uid);
        var placed = new List<Vector2>();
        for (var i = brain.Summons.Count - 1; i >= 0; i--)
        {
            var summon = brain.Summons[i];
            if (Now < summon.At) continue;
            if (!state.Engaged || _mobs.IsDead(uid))
            {
                brain.Summons.RemoveAt(i);
                QueueDel(summon.Circle);
                continue;
            }
            if (!TryComp<Wh40kMarineEffectComponent>(summon.Circle, out var effect) || Now >= effect.EndsAt)
            {
                brain.Summons.RemoveAt(i);
                brain.NextSummonRetry = Now + TimeSpan.FromSeconds(2);
                continue;
            }
            var candidates = SummonCandidates(uid, brain, state, summon.Center.Position)
                .Where(p => placed.All(other => Vector2.DistanceSquared(other, p) >= .81f)).ToList();
            if (candidates.Count == 0)
            {
                brain.Summons[i] = summon with { At = Now + TimeSpan.FromSeconds(.25) };
                continue;
            }
            _random.Shuffle(candidates);
            var point = candidates[0];
            placed.Add(point);
            brain.Summons.RemoveAt(i);
            var entity = Spawn(summon.Prototype, Coordinates(state, point));
            var minion = AddComp<Wh40kMarineMinionComponent>(entity);
            minion.Boss = uid;
            minion.ReadyAt = Now + TimeSpan.FromSeconds(.6);
            if (TryComp<HTNComponent>(entity, out var htn)) htn.Enabled = false;
            brain.SpawnedMinions.TryGetValue(summon.Wave, out var spawned);
            spawned |= 1 << summon.TypeIndex;
            brain.SpawnedMinions[summon.Wave] = spawned;
            if (spawned == (1 << (summon.Wave * 2)) - 1) brain.WavesDone |= summon.Wave;
        }
    }

    private void UpdateMinions()
    {
        var query = EntityQueryEnumerator<Wh40kMarineMinionComponent>();
        while (query.MoveNext(out var uid, out var minion))
        {
            if (Paused(uid)) continue;
            if (minion.Boss is not { } boss || TerminatingOrDeleted(boss) || _mobs.IsDead(boss) || !_arena.Contains(boss, uid))
            {
                QueueDel(uid);
                continue;
            }
            if (TryComp<HTNComponent>(uid, out var htn)) htn.Enabled = Now >= minion.ReadyAt;
            if (TryComp<NPCMeleeCombatComponent>(uid, out var melee) && !CanTarget(boss, melee.Target))
                RemComp<NPCMeleeCombatComponent>(uid);
            if (TryComp<NPCRangedCombatComponent>(uid, out var ranged) && !CanTarget(boss, ranged.Target))
                RemComp<NPCRangedCombatComponent>(uid);
        }
        var effects = EntityQueryEnumerator<Wh40kMarineEffectComponent>();
        while (effects.MoveNext(out var uid, out var effect))
            if (!Paused(uid) && Now >= effect.EndsAt) QueueDel(uid);
    }

    private void ClearOwned(EntityUid uid, Wh40kChaosMarineBrainComponent brain)
    {
        if (TryComp<Wh40kChaosMarineComponent>(uid, out var state)) DestroyMace(uid, brain, state);
        CancelSummons(brain);
        foreach (var seal in brain.Seals) QueueDel(seal.Effect);
        brain.Seals.Clear();
        var minions = EntityQueryEnumerator<Wh40kMarineMinionComponent>();
        while (minions.MoveNext(out var entity, out var component))
            if (component.Boss == uid) QueueDel(entity);
        var effects = EntityQueryEnumerator<Wh40kMarineEffectComponent>();
        while (effects.MoveNext(out var entity, out var effect))
            if (effect.Boss == uid) QueueDel(entity);
    }

    private void CancelSummons(Wh40kChaosMarineBrainComponent brain)
    {
        foreach (var circle in brain.Summons.Select(s => s.Circle).Distinct())
        {
            QueueDel(circle);
        }
        brain.Summons.Clear();
        brain.NextSummonRetry = TimeSpan.Zero;
    }
}
