using Content.Server.Ghost.Roles.Components;
using Content.Server.StationEvents.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WH40K.Activities;
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
    /// A stage-five grid is a disposable set piece, never a shuttle. This direct component audit
    /// closes the route to FTL, docking, a shuttle console, and any future map import that carries
    /// those capabilities.
    /// </summary>
    public bool TryValidateStaticActivityGrid(EntityUid uid, out KoronusActivityRejectReason reason)
    {
        if (!HasComp<MapGridComponent>(uid) ||
            HasComp<ShuttleComponent>(uid) ||
            HasComp<DockingComponent>(uid) ||
            HasComp<FTLComponent>(uid) ||
            HasComp<FTLDestinationComponent>(uid))
        {
            reason = KoronusActivityRejectReason.UnsafeGridContent;
            return false;
        }

        return TryValidateEntity(uid, out reason);
    }
}
