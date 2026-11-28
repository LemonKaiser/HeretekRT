using Content.Shared.Movement.Systems;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Shared._WH40K.TurnInPlace;

/// <summary>Tracks the held turn-in-place key; <see cref="SharedMoverController"/> reads it on movement input.</summary>
public sealed partial class Wh40kTurnInPlaceSystem : EntitySystem
{
    [Dependency] private SharedMoverController _mover = default!;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(Wh40kTurnInPlaceKeys.Wh40kTurnInPlace, new TurnInPlaceInputCmdHandler(this))
            .Register<Wh40kTurnInPlaceSystem>();

        SubscribeLocalEvent<Wh40kTurnInPlaceComponent, PlayerDetachedEvent>((uid, _, _) => RemComp<Wh40kTurnInPlaceComponent>(uid));
        SubscribeLocalEvent<Wh40kTurnInPlaceComponent, LocalPlayerDetachedEvent>((uid, _, _) => RemComp<Wh40kTurnInPlaceComponent>(uid));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<Wh40kTurnInPlaceSystem>();
    }

    public void SetHeld(EntityUid uid, bool held, ushort subTick = 0)
    {
        if (!held)
        {
            RemComp<Wh40kTurnInPlaceComponent>(uid);
            return;
        }

        if (HasComp<Wh40kTurnInPlaceComponent>(uid))
            return;

        AddComp<Wh40kTurnInPlaceComponent>(uid);
        if (!_mover.Wh40kFacingLocked(uid))
            _mover.Wh40kReleaseDirections(uid, subTick);
    }

    private sealed class TurnInPlaceInputCmdHandler(Wh40kTurnInPlaceSystem system) : InputCmdHandler
    {
        public override bool HandleCmdMessage(IEntityManager entManager, ICommonSession? session, IFullInputCmdMessage message)
        {
            if (session?.AttachedEntity is not { } uid)
                return false;

            system.SetHeld(uid, message.State == BoundKeyState.Down, message.SubTick);
            return false;
        }
    }
}
