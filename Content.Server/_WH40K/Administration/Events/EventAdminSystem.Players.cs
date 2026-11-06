using System.Linq;
using System.Numerics;
using Content.Server.Administration.Commands;
using Content.Server.Administration.Systems;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Hands.Systems;
using Content.Shared._WH40K.Administration.Events;
using Content.Shared.Administration;
using Content.Shared.Atmos.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Roles;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.Administration.Events;

public sealed partial class EventAdminSystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private RejuvenateSystem _rejuvenate = default!;
    [Dependency] private SharedGodmodeSystem _godmode = default!;
    [Dependency] private AdminFrozenSystem _frozen = default!;
    [Dependency] private SleepingSystem _sleep = default!;
    [Dependency] private FlammableSystem _fire = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly Dictionary<EntityUid, EntityCoordinates> _returns = new();
    private readonly Dictionary<EntityUid, Effects> _effects = new();
    private sealed class Effects
    {
        public bool? God;
        public (bool Present, bool Muted)? Frozen;
    }

    private EventAdminResult ApplyToPlayers(ICommonSession actor, EventAdminRequest request, List<Guid> targets, int skipped)
    {
        EntityCoordinates? destination = null;
        switch (request.Action)
        {
            case EventAdminAction.Bring:
                if (!TryActorPosition(actor, out var here)) return new("wh40k-event-no-body");
                destination = here;
                break;
            case EventAdminAction.TeleportToPoint:
            case EventAdminAction.GoToPoint:
                if (!_points.TryGetValue(request.Point, out var point) || !ValidPoint(point.Coordinates))
                    return new("wh40k-event-invalid-point");
                destination = point.Coordinates;
                break;
            case EventAdminAction.GiveItem:
                if (request.Number is < 1 or > 10 || !_prototypes.TryIndex<EntityPrototype>(request.Text.Trim(), out var prototype) ||
                    prototype.Abstract || !prototype.Components.ContainsKey("Item"))
                    return new("wh40k-event-invalid-item");
                break;
            case EventAdminAction.EquipOutfit:
                if (!_prototypes.HasIndex<StartingGearPrototype>(request.Text.Trim())) return new("wh40k-event-invalid-outfit");
                break;
            case EventAdminAction.MessageSelection:
                if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > EventAdminLimits.MessageLength) return Invalid();
                break;
        }

        if (request.Action == EventAdminAction.GoToPoint)
            return actor.AttachedEntity is { } admin && Teleport(admin, destination!.Value) ? Done(1) : new("wh40k-event-no-body");
        if (request.Action == EventAdminAction.GoTo)
        {
            if (targets.Count != 1 || !TryBody(targets[0], out var target) || actor.AttachedEntity is not { } admin)
                return new("wh40k-event-select-one");
            return Teleport(admin, Transform(target).Coordinates) ? Done(1) : new("wh40k-event-no-body");
        }
        if (targets.Count == 0) return Done(0, skipped);
        var applied = 0;
        foreach (var id in targets)
        {
            if (request.Action == EventAdminAction.MessageSelection)
            {
                if (_players.TryGetSessionById(new NetUserId(id), out var session) && session.Status != SessionStatus.Disconnected)
                {
                    _chat.DispatchServerMessage(session, request.Text.Trim());
                    applied++;
                }
                else skipped++;
                continue;
            }
            if (!TryBody(id, out var uid) || !ApplyToBody(uid, request, destination)) skipped++;
            else applied++;
        }
        return Done(applied, skipped);
    }

    private bool TryBody(Guid id, out EntityUid uid)
    {
        uid = default;
        if (!_players.TryGetSessionById(new NetUserId(id), out var session) || session.Status != SessionStatus.InGame ||
            session.AttachedEntity is not { } entity || TerminatingOrDeleted(entity)) return false;
        uid = entity;
        return true;
    }

    private bool ApplyToBody(EntityUid uid, EventAdminRequest request, EntityCoordinates? destination)
    {
        switch (request.Action)
        {
            case EventAdminAction.Bring:
            case EventAdminAction.TeleportToPoint:
                return Teleport(uid, destination!.Value);
            case EventAdminAction.Return:
                if (!_returns.TryGetValue(uid, out var previous) || !Teleport(uid, previous, remember: false)) return false;
                _returns.Remove(uid);
                return true;
            case EventAdminAction.Freeze:
            case EventAdminAction.FreezeMute:
            case EventAdminAction.Unfreeze:
                var effects = RememberEffects(uid);
                effects.Frozen ??= (TryComp<AdminFrozenComponent>(uid, out var frozen), frozen?.Muted ?? false);
                if (request.Action == EventAdminAction.Unfreeze) RemComp<AdminFrozenComponent>(uid);
                else if (request.Action == EventAdminAction.FreezeMute) _frozen.FreezeAndMute(uid);
                else EnsureComp<AdminFrozenComponent>(uid);
                return true;
            case EventAdminAction.GodOn:
            case EventAdminAction.GodOff:
                RememberEffects(uid).God ??= HasComp<GodmodeComponent>(uid);
                SetGod(uid, request.Action == EventAdminAction.GodOn);
                return true;
            case EventAdminAction.RestoreEffects:
                if (!_effects.Remove(uid, out var old)) return false;
                if (old.God is { } god) SetGod(uid, god);
                if (old.Frozen is { } freeze)
                {
                    RemComp<AdminFrozenComponent>(uid);
                    if (freeze.Present)
                    {
                        if (freeze.Muted) _frozen.FreezeAndMute(uid);
                        else EnsureComp<AdminFrozenComponent>(uid);
                    }
                }
                return true;
            case EventAdminAction.Heal:
                if (!HasComp<DamageableComponent>(uid)) return false;
                _rejuvenate.PerformRejuvenate(uid);
                return true;
            case EventAdminAction.Sleep: return _sleep.TrySleeping(uid);
            case EventAdminAction.Wake: return _sleep.TryWaking(uid, force: true);
            case EventAdminAction.Extinguish:
                if (!HasComp<FlammableComponent>(uid)) return false;
                _fire.Extinguish(uid);
                return true;
            case EventAdminAction.DropHands:
                if (!TryComp<HandsComponent>(uid, out var hands)) return false;
                foreach (var hand in _hands.EnumerateHands(uid, hands).ToArray())
                    _hands.TryDrop(uid, hand, checkActionBlocker: false, handsComp: hands);
                return true;
            case EventAdminAction.GiveItem:
                for (var i = 0; i < request.Number; i++)
                {
                    var item = Spawn(request.Text.Trim(), Transform(uid).Coordinates);
                    if (HasComp<HandsComponent>(uid)) _hands.TryPickupAnyHand(uid, item, checkActionBlocker: false);
                }
                return true;
            case EventAdminAction.EquipOutfit: return SetOutfitCommand.SetOutfit(uid, request.Text.Trim(), EntityManager);
            default: return false;
        }
    }

    private Effects RememberEffects(EntityUid uid)
    {
        if (!_effects.TryGetValue(uid, out var effects)) _effects[uid] = effects = new Effects();
        return effects;
    }

    private void SetGod(EntityUid uid, bool enabled)
    {
        if (enabled && !HasComp<GodmodeComponent>(uid)) _godmode.EnableGodmode(uid);
        else if (!enabled && HasComp<GodmodeComponent>(uid)) _godmode.DisableGodmode(uid);
    }

    private bool Teleport(EntityUid uid, EntityCoordinates destination, bool remember = true)
    {
        if (TerminatingOrDeleted(uid) || !ValidPoint(destination)) return false;
        var transform = Transform(uid);
        var previous = transform.Coordinates;
        _buckle.Unbuckle((uid, null), null);
        if (TryComp<PullableComponent>(uid, out var pull)) _pulling.TryStopPull(uid, pull);
        if (!_containers.TryRemoveFromContainer((uid, transform, MetaData(uid)), true, out _)) return false;
        if (remember && ValidPoint(previous)) _returns.TryAdd(uid, previous);
        _transform.SetCoordinates(uid, destination);
        _transform.AttachToGridOrMap(uid);
        if (TryComp<PhysicsComponent>(uid, out var body))
        {
            _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
            _physics.SetAngularVelocity(uid, 0f, body: body);
        }
        return true;
    }
}
