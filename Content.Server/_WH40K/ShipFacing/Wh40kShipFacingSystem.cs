using Content.Shared._WH40K.ShipFacing;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.ShipFacing;

/// <summary>
/// Stores the world point each cursor-steering pilot wants their ship to face.
/// </summary>
public sealed partial class Wh40kShipFacingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(0.75);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<Wh40kShipFacingTargetEvent>(OnTarget);
    }

    private void OnTarget(Wh40kShipFacingTargetEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user)
            return;

        if (msg.Target is not { } target
            || !float.IsFinite(target.Position.X)
            || !float.IsFinite(target.Position.Y)
            || !TryComp<PilotComponent>(user, out var pilot)
            || pilot.Console is not { } console)
        {
            RemCompDeferred<Wh40kShipFacingComponent>(user);
            return;
        }

        SetTarget(user, console, target);
    }

    public void SetTarget(EntityUid pilot, EntityUid console, MapCoordinates target)
    {
        var facing = EnsureComp<Wh40kShipFacingComponent>(pilot);
        facing.Target = target;
        facing.Console = console;
        facing.Expires = _timing.CurTime + Lifetime;
    }

    public bool TryGetTarget(Entity<PilotComponent> pilot, out MapCoordinates target)
    {
        target = MapCoordinates.Nullspace;
        if (!TryComp<Wh40kShipFacingComponent>(pilot, out var facing)
            || pilot.Comp.Console != facing.Console
            || _timing.CurTime > facing.Expires)
            return false;

        target = facing.Target;
        return true;
    }
}
