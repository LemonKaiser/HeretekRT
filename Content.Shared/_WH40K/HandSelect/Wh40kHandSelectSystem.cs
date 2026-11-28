using Content.Shared.ActionBlocker;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Shared._WH40K.HandSelect;

/// <summary>Direct left/right hand selection keybinds.</summary>
public sealed partial class Wh40kHandSelectSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(Wh40kHandSelectKeys.Wh40kSelectLeftHand,
                InputCmdHandler.FromDelegate(s => Select(s, HandLocation.Left), handle: false, outsidePrediction: false))
            .Bind(Wh40kHandSelectKeys.Wh40kSelectRightHand,
                InputCmdHandler.FromDelegate(s => Select(s, HandLocation.Right), handle: false, outsidePrediction: false))
            .Register<Wh40kHandSelectSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<Wh40kHandSelectSystem>();
    }

    private void Select(ICommonSession? session, HandLocation side)
    {
        if (session?.AttachedEntity is not { } user || !TryComp<HandsComponent>(user, out var hands))
            return;

        if (!_actionBlocker.CanInteract(user, null))
            return;

        TrySelect((user, hands), side);
    }

    public bool TrySelect(Entity<HandsComponent> user, HandLocation side)
    {
        foreach (var hand in _hands.EnumerateHands(user, user.Comp))
        {
            if (hand.Location != side)
                continue;

            return _hands.TrySetActiveHand(user, hand.Name, user.Comp);
        }

        return false;
    }
}
