using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    private void InitializeTargetFinder()
    {
        SubscribeLocalEvent<TargetFinderHitscanComponent, HitscanRaycastFiredEvent>(OnTargetFinderHitscanHit);
        SubscribeLocalEvent<TargetFinderComponent, MapInitEvent>(OnTargetFinderMapInit);
        SubscribeLocalEvent<TargetFinderComponent, NewLinkEvent>(OnTargetFinderNewLink);
        SubscribeLocalEvent<TargetFinderComponent, PortDisconnectedEvent>(OnTargetFinderPortDisconnected);
    }

    private void OnTargetFinderHitscanHit(Entity<TargetFinderHitscanComponent> entity, ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled || !TryComp<TargetFinderComponent>(args.Gun, out var finder))
            return;

        foreach (var hit in args.HitEntities)
        {
            UpdateTarget((args.Gun, finder), hit);
            return;
        }
    }

    private void OnTargetFinderMapInit(Entity<TargetFinderComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<DeviceLinkSourceComponent>(ent, out var source))
            return;

        foreach (var (sink, links) in source.LinkedPorts)
        {
            if (!HasComp<TargetAssignComponent>(sink))
                continue;

            foreach (var (sourcePort, _) in links)
            {
                if (sourcePort != ent.Comp.LinkingPort)
                    continue;

                ent.Comp.TargetAssigner = sink;
                return;
            }
        }
    }

    private void OnTargetFinderNewLink(EntityUid uid, TargetFinderComponent comp, NewLinkEvent args)
    {
        if (args.Source != uid || args.SourcePort != comp.LinkingPort || !HasComp<TargetAssignComponent>(args.Sink))
            return;

        comp.TargetAssigner = args.Sink;
    }

    private void OnTargetFinderPortDisconnected(EntityUid uid, TargetFinderComponent comp, PortDisconnectedEvent args)
    {
        if (args.Port != comp.LinkingPort || comp.TargetAssigner == null)
            return;

        comp.TargetAssigner = null;
    }

    private void UpdateTarget(Entity<TargetFinderComponent> ent, EntityUid target)
    {
        ent.Comp.Target = target;

        if (ent.Comp.TargetAssigner is { } assigner && TryComp<TargetAssignComponent>(assigner, out var targetAssign))
            targetAssign.Target = target;
    }
}
