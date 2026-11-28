using System.Linq;
using Content.Shared.DoAfter;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Shared._WH40K.CancelInteractions;

/// <summary>
/// Cancels every do-after currently being performed by the player.
/// </summary>
public sealed partial class Wh40kCancelInteractionSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(Wh40kCancelInteractionKeys.Wh40kCancelInteractions,
                InputCmdHandler.FromDelegate(OnCancelKey, handle: false, outsidePrediction: false))
            .Register<Wh40kCancelInteractionSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<Wh40kCancelInteractionSystem>();
    }

    private void OnCancelKey(ICommonSession? session)
    {
        if (session?.AttachedEntity is { } user)
            CancelAll(user);
    }

    public int CancelAll(EntityUid user)
    {
        if (!TryComp<DoAfterComponent>(user, out var comp))
            return 0;

        var cancelled = 0;
        foreach (var index in comp.DoAfters.Keys.ToList())
        {
            if (!_doAfter.IsRunning(user, index, comp))
                continue;

            _doAfter.Cancel(user, index, comp);
            cancelled++;
        }

        return cancelled;
    }
}
