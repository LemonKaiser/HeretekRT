using System.Numerics;
using Content.Shared._WH40K.Bosses;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;

namespace Content.Server._WH40K.ChaosDungeon;

public sealed partial class ChaosDungeonSystem
{
    public static readonly TimeSpan BossSpawnDelay = TimeSpan.FromSeconds(2);

    private void InitializeFog()
    {
        SubscribeLocalEvent<ChaosDungeonFogGateComponent, StartCollideEvent>(OnFogContact);
        SubscribeLocalEvent<ChaosDungeonArenaMemberComponent, MoveEvent>(OnArenaMove);
        SubscribeLocalEvent<ChaosDungeonArenaMemberComponent, EntityTerminatingEvent>(OnArenaMemberTerminating);
    }

    private bool PrepareArena(Entity<ChaosDungeonInstanceComponent> instance)
    {
        var markers = AllEntityQuery<ChaosDungeonBossSpawnComponent, TransformComponent>();
        while (markers.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != instance.Comp.MapId)
                continue;
            instance.Comp.BossSpawn = uid;
            instance.Comp.DungeonGrid = xform.GridUid ?? EntityUid.Invalid;
            break;
        }

        var gates = AllEntityQuery<ChaosDungeonFogGateComponent, TransformComponent>();
        while (gates.MoveNext(out var uid, out var gate, out var xform))
        {
            if (xform.MapID != instance.Comp.MapId)
                continue;
            gate.Instance = instance.Owner;
            instance.Comp.FogGates.Add(uid);
        }
        return instance.Comp.BossSpawn != EntityUid.Invalid && instance.Comp.FogGates.Count > 0;
    }

    private void OnFogContact(Entity<ChaosDungeonFogGateComponent> gate, ref StartCollideEvent args)
    {
        if (args.OurFixtureId == "entry" && args.OtherFixture.Hard)
            TryEnterArena(gate, args.OtherEntity);
    }

    /// <summary>Server-authorized, one-way traversal of an active dungeon's fog threshold.</summary>
    public bool TryEnterArena(EntityUid gateUid, EntityUid player)
    {
        if (!TryComp<ChaosDungeonFogGateComponent>(gateUid, out var gate) ||
            !TryComp<ChaosDungeonInstanceComponent>(gate.Instance, out var instance) ||
            instance.State != ChaosDungeonInstanceState.Active || TerminatingOrDeleted(player) ||
            HasComp<GhostComponent>(player) || HasComp<Wh40kBossComponent>(player) || _mobState.IsDead(player) ||
            !_minds.TryGetMind(player, out _, out var mind) || mind.UserId == null ||
            Transform(player).MapID != instance.MapId || !TryGetFogDestination((gateUid, gate), out var destination))
            return false;

        var local = Vector2.Transform(_transform.GetWorldPosition(player), _transform.GetInvWorldMatrix(gateUid));
        // The trigger is outside the hard barrier. Touching it from inside is never an exit.
        if (local.Y < 0.45f || local.Y > 2f || MathF.Abs(local.X) > 0.9f ||
            HasComp<ChaosDungeonArenaMemberComponent>(player))
            return false;

        MovePlayer(player, destination);
        var member = EnsureComp<ChaosDungeonArenaMemberComponent>(player);
        member.Gate = gateUid;
        instance.ArenaMembers.Add(player);
        if (!instance.BossStarted)
        {
            instance.BossStarted = true;
            instance.BossSpawnAt = _timing.CurTime + BossSpawnDelay;
            instance.BossSpawnGate = gateUid;
        }
        return true;
    }

    private bool TryGetFogDestination(Entity<ChaosDungeonFogGateComponent> gate, out EntityCoordinates destination)
    {
        destination = default;
        var transform = Transform(gate);
        if (transform.GridUid is not { } grid || !_maps.MapExists(transform.MapID))
            return false;

        // Parent the entrant to the grid, never to the disposable gate itself.
        destination = _transform.WithEntityId(new EntityCoordinates(gate.Owner, gate.Comp.ExitOffset), grid);
        return _turf.TryGetTileRef(destination, out var tile) && !_turf.IsSpace(tile.Value) &&
               !_turf.IsTileBlocked(tile.Value, CollisionGroup.MobMask);
    }

    private void SpawnArenaBoss(Entity<ChaosDungeonInstanceComponent> instance, EntityUid gate)
    {
        if (!TryComp<ChaosDungeonBossSpawnComponent>(instance.Comp.BossSpawn, out var marker))
            return;
        var boss = Spawn(marker.Prototype, Transform(instance.Comp.BossSpawn).Coordinates);
        instance.Comp.Boss = boss;
        if (TryComp<Wh40kBossArenaComponent>(boss, out var arena) && arena.Anchor is { } anchor)
        {
            arena.Center = _transform.WithEntityId(
                new EntityCoordinates(instance.Comp.BossSpawn, marker.ArenaCenterOffset), anchor).Position;
            Dirty(boss, arena);
        }
        var member = EnsureComp<ChaosDungeonArenaMemberComponent>(boss);
        member.Gate = gate;
        member.IsBoss = true;
        instance.Comp.ArenaMembers.Add(boss);
    }

    private void OnArenaMove(Entity<ChaosDungeonArenaMemberComponent> ent, ref MoveEvent args)
    {
        if (ent.Comp.Returning || TerminatingOrDeleted(ent.Owner) ||
            !TryComp<ChaosDungeonFogGateComponent>(ent.Comp.Gate, out var gate) || TerminatingOrDeleted(gate.Instance) ||
            !TryComp<ChaosDungeonInstanceComponent>(gate.Instance, out var instance) ||
            instance.State != ChaosDungeonInstanceState.Active)
            return;
        var xform = Transform(ent);
        // Normal dungeon evacuation may move a player to another map.
        if (!ent.Comp.IsBoss && xform.MapID != instance.MapId)
            return;
        var local = Vector2.Transform(_transform.GetWorldPosition(ent), _transform.GetInvWorldMatrix(ent.Comp.Gate));
        if (xform.MapID == instance.MapId && local.Y <= -0.6f)
            return;

        EntityCoordinates destination;
        var previous = args.OldPosition.IsValid(EntityManager)
            ? _transform.ToMapCoordinates(args.OldPosition) : MapCoordinates.Nullspace;
        if (previous.MapId == instance.MapId && Transform(ent.Comp.Gate).GridUid is { } grid &&
            Vector2.Transform(previous.Position, _transform.GetInvWorldMatrix(ent.Comp.Gate)).Y <= -0.6f)
        {
            // Return to the last arena position even if somebody has blocked the entrance's landing tile.
            destination = _transform.WithEntityId(args.OldPosition, grid);
        }
        else if (!TryGetFogDestination((ent.Comp.Gate, gate), out destination))
        {
            return;
        }

        ent.Comp.Returning = true;
        try
        {
            MovePlayer(ent, destination);
        }
        finally
        {
            ent.Comp.Returning = false;
        }
    }

    private void OnArenaMemberTerminating(Entity<ChaosDungeonArenaMemberComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!ent.Comp.IsBoss || !TryComp<ChaosDungeonFogGateComponent>(ent.Comp.Gate, out var gate) ||
            TerminatingOrDeleted(gate.Instance) ||
            !TryComp<ChaosDungeonInstanceComponent>(gate.Instance, out var instance) ||
            instance.State != ChaosDungeonInstanceState.Active || instance.Boss != ent.Owner ||
            !_instances.TryGetValue(instance.SourceShip, out var active) || active.Owner != gate.Instance)
            return;
        CompleteArena((gate.Instance, instance));
    }

    private void CompleteArena(Entity<ChaosDungeonInstanceComponent> instance)
    {
        instance.Comp.State = ChaosDungeonInstanceState.Completed;
        RemoveFog(instance.Comp);
        var extraction = _timing.CurTime + ExtractionDuration;
        instance.Comp.ExtractionAt = extraction < instance.Comp.ExpiresAt ? extraction : instance.Comp.ExpiresAt;
        SetParticipantTimers(instance.Comp, instance.Comp.ExtractionAt, extraction: true);
        UpdateExtraction(instance);
    }

    private void RemoveFog(ChaosDungeonInstanceComponent instance)
    {
        instance.BossSpawnAt = null;
        foreach (var member in instance.ArenaMembers)
        {
            if (!TerminatingOrDeleted(member))
                RemComp<ChaosDungeonArenaMemberComponent>(member);
        }
        instance.ArenaMembers.Clear();
        foreach (var gate in instance.FogGates)
        {
            if (!TerminatingOrDeleted(gate))
                Del(gate);
        }
        instance.FogGates.Clear();
    }
}
