using Content.Shared.Interaction;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Content.Shared._WH40K.SectorMap.Components;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// RCD edits bypass ConstructionSystem, so they need their own authoritative safe-sector gate.
/// </summary>
public sealed class KoronusSafetyRcdSystem : EntitySystem
{
    [Dependency] private KoronusSafetyPolicySystem _safety = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RCDComponent, ComponentStartup>(OnRcdStartup);
        SubscribeLocalEvent<KoronusSafetyRcdComponent, AfterInteractEvent>(OnAfterInteract, before: [typeof(RCDSystem)]);
    }

    private void OnRcdStartup(EntityUid uid, RCDComponent component, ComponentStartup args)
    {
        EnsureComp<KoronusSafetyRcdComponent>(uid);
    }

    private void OnAfterInteract(EntityUid uid, KoronusSafetyRcdComponent component, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        var target = args.Target ?? args.ClickLocation.EntityId;
        if (_safety.ShouldBlockGridModification(args.User, target))
            args.Handled = true;
    }
}
