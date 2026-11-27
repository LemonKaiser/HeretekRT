using Content.Server._WH40K.Augments;
using Content.Server.Popups;
using Content.Shared._WH40K.Biotype;
using Content.Shared._WH40K.Synthetics;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Synthetics;

/// <summary>
/// Manages the Mechanicus Cogitator Chassis Diagnostics Panel for Cybernetic (Mechanicus) and IPC bodies:
/// grants <see cref="ActionId"/> on spawn/biotype conversion, allows Tech-Priests to link in with a multitool,
/// pushes periodic UI state, and drives Overclock, Reboot, Isolation, Detach, and Cybernetic HUD telemetry.
/// </summary>
public sealed partial class Wh40kChassisPanelSystem : SharedWh40kSyntheticsSystem
{
    public static readonly EntProtoId ActionId = "ActionWh40kChassisPanel";
    public const string BoundUserInterface = "Wh40kChassisPanelBoundUserInterface";

    private static readonly TimeSpan StateInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan LinkDelay = TimeSpan.FromSeconds(2);
    private const string LinkQuality = "Pulsing";

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedToolSystem _tools = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly Wh40kAugmentBedSystem _augmentBed = default!;
    [Dependency] private readonly Wh40kChassisIsolationSystem _isolation = default!;
    [Dependency] private readonly Wh40kSyntheticLimbSystem _limbs = default!;

    private TimeSpan _nextState;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HumanoidAppearanceComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<Wh40kBiotypeComponent, Wh40kBiotypeAppliedEvent>(OnBiotypeApplied);
        SubscribeLocalEvent<Wh40kChassisPanelActionsComponent, Wh40kOpenChassisPanelActionEvent>(OnOpenAction);
        SubscribeLocalEvent<Wh40kChassisComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
        SubscribeLocalEvent<Wh40kChassisComponent, Wh40kChassisPanelLinkDoAfterEvent>(OnLinkDoAfter);

        Subs.BuiEvents<Wh40kChassisComponent>(Wh40kChassisPanelUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<BoundUIClosedEvent>(OnUiClosed);
            subs.Event<Wh40kChassisIsolatePartMessage>(OnIsolateMessage);
            subs.Event<Wh40kChassisDetachPartMessage>(OnDetachMessage);
            subs.Event<Wh40kChassisRebootMessage>(OnRebootMessage);
            subs.Event<Wh40kChassisOverclockMessage>(OnOverclockMessage);
            subs.Event<Wh40kChassisInstallAugmentMessage>(OnInstallAugmentMessage);
        });
    }

    private void OnMapInit(Entity<HumanoidAppearanceComponent> ent, ref MapInitEvent args)
    {
        if (IsSyntheticChassis(ent))
            Grant(ent);
    }

    private void OnBiotypeApplied(Entity<Wh40kBiotypeComponent> ent, ref Wh40kBiotypeAppliedEvent args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (args.Biotype == Wh40kBiotype.Cybernetic && IsSyntheticChassis(ent))
            Grant(ent);
        else
            Revoke(ent);
    }

    public void Grant(EntityUid body)
    {
        EnsureComp<Wh40kChassisComponent>(body);
        EnsureComp<Wh40kCyberneticHudComponent>(body);
        var actions = EnsureComp<Wh40kChassisPanelActionsComponent>(body);
        if (actions.Action is not { } existing || !Exists(existing))
        {
            actions.Action = null;
            _actions.AddAction(body, ref actions.Action, ActionId);
        }

        if (!_ui.HasUi(body, Wh40kChassisPanelUiKey.Key))
        {
            _ui.SetUi(body, Wh40kChassisPanelUiKey.Key, new InterfaceData(BoundUserInterface, requireInputValidation: false));
        }

        SyncHud(body);
    }

    public void Revoke(EntityUid body)
    {
        if (TryComp<Wh40kChassisPanelActionsComponent>(body, out var actions))
        {
            _actions.RemoveAction(body, actions.Action);
            RemComp<Wh40kChassisPanelActionsComponent>(body);
        }

        if (!HasComp<Wh40kChassisComponent>(body))
            return;

        _ui.CloseUi(body, Wh40kChassisPanelUiKey.Key);
        RemComp<Wh40kChassisRebootComponent>(body);
        RemComp<Wh40kChassisOverclockComponent>(body);
        RemComp<Wh40kCyberneticHudComponent>(body);
        RemComp<Wh40kChassisComponent>(body);
        _speed.RefreshMovementSpeedModifiers(body);
    }

    private void OnOpenAction(Entity<Wh40kChassisPanelActionsComponent> ent, ref Wh40kOpenChassisPanelActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!IsSyntheticChassis(ent) || !HasComp<Wh40kChassisComponent>(ent))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-synthetics-not-synthetic"), ent, ent);
            return;
        }

        if (_mobState.IsDead(ent))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-panel-refuse-dead"), ent, ent);
            return;
        }

        _ui.OpenUi(ent.Owner, Wh40kChassisPanelUiKey.Key, ent.Owner);
    }

    private void OnAfterInteractUsing(Entity<Wh40kChassisComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || args.User == ent.Owner || !_tools.HasQuality(args.Used, LinkQuality))
            return;

        args.Handled = TryStartLink(ent, args.User, args.Used);
    }

    public bool TryStartLink(EntityUid body, EntityUid user, EntityUid tool)
    {
        if (!IsSyntheticChassis(body))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-synthetics-not-synthetic"), body, user);
            return true;
        }

        _popup.PopupEntity(Loc.GetString("wh40k-chassis-link-start-user", ("target", body)), body, user);
        _popup.PopupEntity(
            Loc.GetString("wh40k-chassis-link-start-target", ("user", user)),
            body,
            body,
            PopupType.Medium);

        return _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            user,
            LinkDelay,
            new Wh40kChassisPanelLinkDoAfterEvent(),
            body,
            body,
            tool)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnLinkDoAfter(Entity<Wh40kChassisComponent> ent, ref Wh40kChassisPanelLinkDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        Link(ent, args.User);
    }

    internal bool CanUsePanel(EntityUid body, EntityUid actor)
    {
        return CanAccessPanel(body, actor) &&
            (actor == body || CompOrNull<Wh40kChassisPanelActionsComponent>(body)?.LinkedOperators.Contains(actor) == true);
    }

    private bool CanAccessPanel(EntityUid body, EntityUid actor)
    {
        if (actor == body)
            return !_mobState.IsDead(body);

        return _actionBlocker.CanInteract(actor, body) && _interaction.InRangeUnobstructed(actor, body);
    }

    public void Link(EntityUid body, EntityUid user)
    {
        if (!CanAccessPanel(body, user))
            return;

        Grant(body);
        OpenPanel(body, user);
        _popup.PopupEntity(
            Loc.GetString("wh40k-chassis-link-done-target", ("user", user)),
            body,
            body,
            PopupType.MediumCaution);
    }

    /// <summary>
    /// Opens a cybernetic patient's chassis diagnostics panel in operating-table mode, focusing the Augmentation tab.
    /// </summary>
    public void OpenFromAugmentTable(EntityUid body, Entity<Wh40kAugmentBedComponent> bed, EntityUid actor)
    {
        if (!IsSyntheticChassis(body) || (actor != body && !CanAccessPanel(body, actor)))
            return;

        if (actor == body && _mobState.IsDead(body))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-panel-refuse-dead"), body, actor);
            return;
        }

        Grant(body);
        if (TryComp<Wh40kChassisComponent>(body, out var chassis))
            chassis.AugmentFocusSeq++;

        var powered = _augmentBed.IsOnPoweredTable(body);
        _audio.PlayPvs(OverclockSound, bed);
        _popup.PopupEntity(
            Loc.GetString(powered ? "wh40k-chassis-table-linked" : "wh40k-chassis-table-linked-unpowered"),
            bed,
            actor);
        OpenPanel(body, actor);
        PushState(body);
    }

    private void OpenPanel(EntityUid body, EntityUid actor)
    {
        if (actor != body)
            Comp<Wh40kChassisPanelActionsComponent>(body).LinkedOperators.Add(actor);

        _ui.OpenUi(body, Wh40kChassisPanelUiKey.Key, actor);
    }

    private void OnUiOpened(Entity<Wh40kChassisComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!CanUsePanel(ent, args.Actor))
        {
            _ui.CloseUi(ent.Owner, Wh40kChassisPanelUiKey.Key, args.Actor);
            return;
        }

        PushState(ent);
    }

    private void OnUiClosed(Entity<Wh40kChassisComponent> ent, ref BoundUIClosedEvent args)
    {
        CompOrNull<Wh40kChassisPanelActionsComponent>(ent)?.LinkedOperators.Remove(args.Actor);
    }

    public void PushState(EntityUid body)
    {
        SyncHud(body);
        if (!_ui.IsUiOpen(body, Wh40kChassisPanelUiKey.Key))
            return;

        _ui.SetUiState(body, Wh40kChassisPanelUiKey.Key, BuildState(body));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        UpdateReboots(now);

        if (now < _nextState)
            return;

        _nextState = now + StateInterval;
        UpdateOverclock(now);
        CoolChassisHeat((float) StateInterval.TotalSeconds);

        var query = EntityQueryEnumerator<Wh40kChassisComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            SyncHud(uid);
            if (TryComp<UserInterfaceComponent>(uid, out var ui) &&
                _ui.IsUiOpen((uid, ui), Wh40kChassisPanelUiKey.Key))
            {
                _ui.SetUiState((uid, ui), Wh40kChassisPanelUiKey.Key, BuildState(uid));
            }
        }
    }
}

[RegisterComponent]
public sealed partial class Wh40kChassisPanelActionsComponent : Component
{
    [DataField]
    public EntityUid? Action;

    public readonly HashSet<EntityUid> LinkedOperators = new();
}
