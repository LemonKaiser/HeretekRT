using Content.Server.Humanoid.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Spawners.Components;
using Content.Server.Ghost.Roles.Components;
using Content.Server._WH40K.Activities.Components;
using Content.Server.Station.Components;
using Content.Server.StationEvents.Components;
using Content.Shared._WH40K.Activities;
using Content.Shared.Ghost.Roles.Components;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._WH40K.Activities;

/// <summary>
/// Fail-closed component audit used by future map/entity loaders. It is intentionally separate
/// from activity selection so an external map cannot bypass the RT no-ghost rule after selection.
/// </summary>
public sealed class ActivityContentAudit : EntitySystem
{
    public bool TryValidateEntity(EntityUid uid, out KoronusActivityRejectReason reason)
    {
        if (HasComp<GhostRoleComponent>(uid) ||
            HasComp<GhostTakeoverAvailableComponent>(uid) ||
            HasComp<SentienceTargetComponent>(uid))
        {
            reason = KoronusActivityRejectReason.GhostContent;
            return false;
        }

        reason = KoronusActivityRejectReason.None;
        return true;
    }

    /// <summary>
    /// An ordinary disposable set piece is never a shuttle. This direct component audit closes the
    /// route to FTL, docking, a shuttle console, and any future map import that carries those
    /// capabilities.
    /// </summary>
    public bool TryValidateStaticActivityGrid(EntityUid uid, out KoronusActivityRejectReason reason)
    {
        if (!HasComp<MapGridComponent>(uid))
        {
            reason = KoronusActivityRejectReason.UnsafeGridContent;
            return false;
        }

        return TryValidateSetPieceEntity(uid, out reason);
    }

    /// <summary>
    /// Map loading attaches the ordinary shuttle capability to a grid even when an authored grid
    /// contains no shuttle component. Stage-two locations explicitly strip that engine default
    /// before the grid can be accepted; an imported child with the same capabilities still fails
    /// the audit rather than being silently trusted.
    /// </summary>
    public bool TrySanitizeLoadedSetPieceGrid(EntityUid uid, out KoronusActivityRejectReason reason)
    {
        if (!HasComp<MapGridComponent>(uid))
        {
            reason = KoronusActivityRejectReason.UnsafeGridContent;
            return false;
        }

        RemComp<ShuttleComponent>(uid);
        RemComp<DockingComponent>(uid);
        RemComp<FTLComponent>(uid);
        RemComp<FTLDestinationComponent>(uid);
        RemComp<BecomesStationComponent>(uid);
        return TryValidateStaticActivityGrid(uid, out reason);
    }

    /// <summary>
    /// Curates an explicitly whitelisted stock map before it becomes an RT activity. Stock ruins
    /// are useful for their authored rooms, damage and environmental storytelling, but their
    /// loot, NPC and random-spawn payloads do not belong to a fixed no-reward activity. Those
    /// payloads are deleted immediately, then every surviving child must pass the normal
    /// fail-closed capability audit.
    /// </summary>
    public bool TrySanitizeImportedSetPieceContents(EntityUid grid, out KoronusActivityRejectReason reason)
    {
        if (!HasComp<MapGridComponent>(grid))
        {
            reason = KoronusActivityRejectReason.UnsafeGridContent;
            return false;
        }

        var discarded = new List<EntityUid>();
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var transform))
        {
            if (uid == grid || !IsOnGrid(transform, grid))
                continue;

            if (ShouldDiscardImportedSetPieceEntity(uid))
                discarded.Add(uid);
        }

        foreach (var uid in discarded)
        {
            if (Exists(uid))
                Del(uid);
        }

        query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var transform))
        {
            if (uid != grid && IsOnGrid(transform, grid) && !TryValidateSetPieceEntity(uid, out reason))
                return false;
        }

        reason = KoronusActivityRejectReason.None;
        return true;
    }

    /// <summary>
    /// Audits every entity imported from an authored activity map, not just the root grid. A
    /// harmless-looking child entity cannot smuggle in a shuttle, dock, FTL endpoint or station
    /// declaration that the grid-root check would otherwise miss.
    /// </summary>
    public bool TryValidateSetPieceEntity(EntityUid uid, out KoronusActivityRejectReason reason)
    {
        if (HasComp<ShuttleComponent>(uid) ||
            HasComp<DockingComponent>(uid) ||
            HasComp<FTLComponent>(uid) ||
            HasComp<FTLDestinationComponent>(uid) ||
            HasComp<BecomesStationComponent>(uid))
        {
            reason = KoronusActivityRejectReason.UnsafeGridContent;
            return false;
        }

        return TryValidateEntity(uid, out reason);
    }

    private bool ShouldDiscardImportedSetPieceEntity(EntityUid uid)
    {
        // An activity's declared objective is its only transferable reward. This is deliberately
        // component based instead of a prototype-name deny-list, so a future stock-map edit cannot
        // silently add an item, NPC or random table to the curated activity pool.
        return HasComp<ItemComponent>(uid) ||
               HasComp<MobStateComponent>(uid) ||
               HasComp<GhostRoleComponent>(uid) ||
               HasComp<GhostTakeoverAvailableComponent>(uid) ||
               HasComp<SentienceTargetComponent>(uid) ||
               HasComp<RandomHumanoidSpawnerComponent>(uid) ||
               HasComp<GhostRoleMobSpawnerComponent>(uid) ||
               HasComp<ConditionalSpawnerComponent>(uid) ||
               HasComp<EntityTableSpawnerComponent>(uid) ||
               HasComp<TimedSpawnerComponent>(uid) ||
               HasComp<RandomDecalSpawnerComponent>(uid) ||
               HasComp<ShuttleConsoleComponent>(uid) ||
               HasComp<ThrusterComponent>(uid);
    }

    private static bool IsOnGrid(TransformComponent transform, EntityUid grid)
    {
        return transform.GridUid == grid || transform.ParentUid == grid;
    }

    /// <summary>
    /// The survey cutter is the sole stage-five docking exception. It can retain exactly one
    /// tagged, receive-only airlock; all other imported entities follow the ordinary no-docking
    /// policy. This does not permit a shuttle, FTL endpoint or station declaration.
    /// </summary>
    public bool TryValidateDockableSetPieceEntity(EntityUid uid, out KoronusActivityRejectReason reason)
    {
        if (HasComp<DockingComponent>(uid))
        {
            if (!TryComp<KoronusDockableActivityPortComponent>(uid, out _) ||
                !TryComp<DockingComponent>(uid, out var dock) ||
                !dock.ReceiveOnly ||
                dock.DockType != DockType.Airlock ||
                HasComp<ShuttleComponent>(uid) ||
                HasComp<FTLComponent>(uid) ||
                HasComp<FTLDestinationComponent>(uid) ||
                HasComp<BecomesStationComponent>(uid))
            {
                reason = KoronusActivityRejectReason.UnsafeGridContent;
                return false;
            }

            return TryValidateEntity(uid, out reason);
        }

        if (HasComp<KoronusDockableActivityPortComponent>(uid))
        {
            reason = KoronusActivityRejectReason.UnsafeGridContent;
            return false;
        }

        return TryValidateSetPieceEntity(uid, out reason);
    }

}
