using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Network;

namespace Content.Shared._WH40K.Fishing;

/// <summary>
/// Starts fishing from a map-authored spot. Catch creation is server-authoritative.
/// </summary>
public sealed class SharedFishingSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popups = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<FishingRodComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<FishingRodComponent, FishingDoAfterEvent>(OnFishingCompleted);
    }

    private void OnAfterInteract(Entity<FishingRodComponent> rod, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<FishingSpotComponent>(target))
            return;

        var doAfter = new DoAfterArgs(EntityManager, args.User, rod.Comp.CatchTime, new FishingDoAfterEvent(), rod.Owner,
            target, rod.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            args.Handled = true;
    }

    private void OnFishingCompleted(Entity<FishingRodComponent> rod, ref FishingDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !_net.IsServer || args.Target is not { } target ||
            !TryComp<FishingSpotComponent>(target, out var spot))
        {
            return;
        }

        if (!Exists(args.User) || !Exists(args.Used) || args.Used != rod.Owner)
            return;

        var fish = Spawn(spot.Catch, Transform(args.User).Coordinates);
        _popups.PopupEntity(Loc.GetString("fishing-catch-success", ("fish", Name(fish))), args.User, args.User);
        args.Handled = true;
    }
}
