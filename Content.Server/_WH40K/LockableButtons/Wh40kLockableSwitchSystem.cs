using Content.Shared.Access.Systems;
using Content.Shared.Lock;
using Content.Shared.Popups;

namespace Content.Server._WH40K.LockableButtons;

/// <summary>
/// Gates a locked button or switch: users admitted by its access reader press it while keeping it locked; others are denied.
/// </summary>
public sealed partial class Wh40kLockableSwitchSystem : EntitySystem
{
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LockComponent, Wh40kSwitchPressAttemptEvent>(OnPressAttempt);
    }

    private void OnPressAttempt(Entity<LockComponent> ent, ref Wh40kSwitchPressAttemptEvent args)
    {
        if (!ent.Comp.Locked)
            return;

        if (ent.Comp.UnlockOnClick)
        {
            args.Cancelled = true;
            return;
        }

        if (_accessReader.IsAllowed(args.User, ent))
            return;

        _popup.PopupEntity(Loc.GetString("lock-comp-has-user-access-fail"), ent, args.User);
        args.Cancelled = true;
        args.Handled = true;
    }
}

[ByRefEvent]
public record struct Wh40kSwitchPressAttemptEvent(EntityUid User, bool Cancelled = false, bool Handled = false);
