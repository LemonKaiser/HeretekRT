using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Pinpointer;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WH40K.ShipAccess;
using Content.Shared.Access.Components;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleLockSystem
{
    [Dependency] private SharedUserInterfaceSystem _shipAccessUi = default!;
    [Dependency] private EntityLookupSystem _shipAccessLookup = default!;
    [Dependency] private SharedInteractionSystem _shipAccessInteraction = default!;
    [Dependency] private NavMapSystem _shipAccessNavMap = default!;
    [Dependency] private IAdminLogManager _shipAccessLog = default!;

    private const float GuestRange = 2f;
    private static readonly TimeSpan ShipAccessRefreshInterval = TimeSpan.FromSeconds(1);
    private readonly Dictionary<(EntityUid Console, EntityUid Actor), TimeSpan> _shipAccessRefreshTimes = new();

    private void InitializeShipAccessPanel()
    {
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<Wh40kShipAccessRefresh>(OnShipAccessRefresh);
            subs.Event<Wh40kShipAccessRequest>(OnShipAccessRequest);
        });
    }

    internal void ClearShipAccessRefresh(EntityUid console, EntityUid actor)
    {
        _shipAccessRefreshTimes.Remove((console, actor));
    }

    private void OnShipAccessRefresh(Entity<ShuttleConsoleComponent> console, ref Wh40kShipAccessRefresh args)
    {
        var key = (console.Owner, args.Actor);
        if (_shipAccessRefreshTimes.TryGetValue(key, out var next) && next > Timing.CurTime)
            return;

        _shipAccessRefreshTimes[key] = Timing.CurTime + ShipAccessRefreshInterval;
        SendShipAccessSnapshot(console, args.Actor);
    }

    private void OnShipAccessRequest(Entity<ShuttleConsoleComponent> console, ref Wh40kShipAccessRequest args)
    {
        if (!TryGetAccessShip(console, out var grid, out var deed, out var lockComponent) ||
            !CanManageShipAccess(console, args.Actor, lockComponent))
        {
            Popup.PopupEntity(Loc.GetString("shuttle-console-access-denied"), console, args.Actor);
            SendShipAccessSnapshot(console, args.Actor);
            return;
        }

        var changed = args.Action switch
        {
            Wh40kShipAccessAction.ProtectShip or Wh40kShipAccessAction.RestoreShip =>
                TryToggleShipAccess(console, args.Actor, lockComponent, args.Action == Wh40kShipAccessAction.ProtectShip),
            Wh40kShipAccessAction.ProtectDoor or Wh40kShipAccessAction.RestoreDoor =>
                TrySetDoorProtection(grid, args.Target, args.Action == Wh40kShipAccessAction.ProtectDoor),
            Wh40kShipAccessAction.GrantGuest => TryGrantPanelGuest(console, grid, args.Actor, deed, args.Target),
            Wh40kShipAccessAction.RevokeGuest => TryRevokePanelGuest(grid, args.Target),
            Wh40kShipAccessAction.RestrictGuestGrant or Wh40kShipAccessAction.AllowGuestSelfGrant =>
                SetGuestGrantRestriction(grid, args.Action == Wh40kShipAccessAction.RestrictGuestGrant),
            _ => false,
        };

        if (changed)
        {
            _shipAccessLog.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(args.Actor):player} changed access on {ToPrettyString(grid):grid}: {args.Action} {args.Target}");
        }
        else
            Popup.PopupEntity(Loc.GetString("wh40k-ship-access-stale"), console, args.Actor);

        SendShipAccessSnapshot(console, args.Actor);
    }

    private bool TryGetAccessShip(EntityUid console, out EntityUid grid, out ShuttleDeedComponent deed,
        out ShuttleConsoleLockComponent lockComponent)
    {
        grid = default;
        deed = default!;
        lockComponent = default!;

        // The stock lock and guest verbs act on the console's own grid.
        var ev = new ConsoleShuttleEvent { Console = console };
        RaiseLocalEvent(console, ref ev);
        if (ev.Console != console || Transform(console).GridUid is not { } consoleGrid ||
            !TryComp<ShuttleConsoleLockComponent>(console, out var consoleLock) ||
            !TryComp<ShuttleDeedComponent>(consoleGrid, out var shipDeed) || shipDeed.ShuttleUid != consoleGrid)
            return false;

        grid = consoleGrid;
        deed = shipDeed;
        lockComponent = consoleLock;
        return true;
    }

    private bool CanManageShipAccess(EntityUid console, EntityUid actor, ShuttleConsoleLockComponent lockComponent)
    {
        return !GetEffectiveLockState(console, lockComponent) && (HasDeedAccess(console, actor, lockComponent) ||
            Transform(console).GridUid is { } grid && HasStoredShuttleOwnerAccess(grid, actor));
    }

    private void SendShipAccessSnapshot(EntityUid console, EntityUid actor)
    {
        if (!TryGetAccessShip(console, out var grid, out var deed, out var lockComponent))
        {
            _shipAccessUi.ServerSendUiMessage(console, ShuttleConsoleUiKey.Key,
                new Wh40kShipAccessSnapshot(null, null, false, false, 0, 0, [], [], []), actor);
            return;
        }

        var canManage = CanManageShipAccess(console, actor, lockComponent);
        _shipAccessNavMap.EnsureGridMap(grid);
        var doors = new List<Wh40kShipAccessDoor>();
        var totalReaders = 0;
        var protectedReaders = 0;
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp<ShipAccessReaderComponent>(child, out var reader))
                continue;

            totalReaders++;
            if (reader.Enabled)
                protectedReaders++;

            var xform = Transform(child);
            if (xform.Anchored && HasComp<DoorComponent>(child))
                doors.Add(new Wh40kShipAccessDoor(GetNetEntity(child), Name(child), xform.LocalPosition, reader.Enabled));
        }

        var guests = new List<Wh40kShipAccessGuest>();
        var nearby = new List<Wh40kShipAccessGuest>();
        if (canManage)
        {
            TryComp<ShipGuestAccessComponent>(grid, out var guestAccess);
            if (guestAccess != null)
            {
                foreach (var credential in guestAccess.GuestIdCards.Concat(guestAccess.GuestCyborgs).Where(credential => Exists(credential)).Distinct())
                    guests.Add(new Wh40kShipAccessGuest(GetNetEntity(credential), GuestCredentialName(credential)));
            }

            foreach (var (candidate, _) in _shipAccessLookup.GetEntitiesInRange<MobStateComponent>(Transform(console).Coordinates, GuestRange))
            {
                if (!IsNearbyGuest(console, grid, actor, candidate))
                    continue;

                foreach (var credential in GetGuestCredentials(candidate, deed, guestAccess))
                    nearby.Add(new Wh40kShipAccessGuest(GetNetEntity(credential), GuestCredentialName(credential)));
            }
        }

        var snapshot = new Wh40kShipAccessSnapshot(GetNetEntity(grid), deed.ShuttleOwner, canManage, GuestSelfGrantRestricted(grid),
            protectedReaders, totalReaders,
            doors.OrderBy(door => door.Name, StringComparer.Ordinal).ThenBy(door => door.Position.Y).ThenBy(door => door.Position.X).ToArray(),
            guests.OrderBy(guest => guest.Name, StringComparer.Ordinal).ThenBy(guest => guest.Id).ToArray(),
            nearby.DistinctBy(guest => guest.Id).OrderBy(guest => guest.Name, StringComparer.Ordinal).ThenBy(guest => guest.Id).ToArray());
        _shipAccessUi.ServerSendUiMessage(console, ShuttleConsoleUiKey.Key, snapshot, actor);
    }

    private bool IsNearbyGuest(EntityUid console, EntityUid grid, EntityUid actor, EntityUid candidate)
    {
        return actor != candidate && TryComp<MobStateComponent>(candidate, out var mob) && mob.CurrentState != MobState.Dead &&
            Transform(candidate).GridUid == grid &&
            _shipAccessInteraction.InRangeUnobstructed(console, candidate, GuestRange, popup: false);
    }

    private IEnumerable<EntityUid> GetGuestCredentials(EntityUid candidate, ShuttleDeedComponent deed, ShipGuestAccessComponent? guests)
    {
        if (HasComp<BorgChassisComponent>(candidate))
        {
            if (guests?.GuestCyborgs.Contains(candidate) != true)
                yield return candidate;
            yield break;
        }

        var cards = FindAccessibleIdCards(candidate);
        if (cards.Any(card => TryComp<ShuttleDeedComponent>(card, out var cardDeed) && cardDeed.ShuttleUid == deed.ShuttleUid))
            yield break;

        foreach (var card in cards)
        {
            if (HasComp<IdCardComponent>(card) && guests?.GuestIdCards.Contains(card) != true)
                yield return card;
        }
    }

    private string GuestCredentialName(EntityUid credential)
    {
        if (TryComp<IdCardComponent>(credential, out var card) && !string.IsNullOrWhiteSpace(card.FullName))
            return card.FullName;

        return Name(credential);
    }

    private bool TryGrantPanelGuest(EntityUid console, EntityUid grid, EntityUid actor, ShuttleDeedComponent deed, NetEntity target)
    {
        if (!TryGetEntity(target, out var credential))
            return false;

        TryComp<ShipGuestAccessComponent>(grid, out var guestAccess);
        foreach (var (candidate, _) in _shipAccessLookup.GetEntitiesInRange<MobStateComponent>(Transform(console).Coordinates, GuestRange))
        {
            if (!IsNearbyGuest(console, grid, actor, candidate) || !GetGuestCredentials(candidate, deed, guestAccess).Contains(credential.Value))
                continue;

            guestAccess ??= EnsureComp<ShipGuestAccessComponent>(grid);
            var credentials = credential == candidate ? guestAccess.GuestCyborgs : guestAccess.GuestIdCards;
            if (!credentials.Add(credential.Value))
                return false;

            Dirty(grid, guestAccess);
            Popup.PopupEntity(Loc.GetString("shuttle-console-guest-access-granted"), console, candidate);
            return true;
        }

        return false;
    }

    private bool TryRevokePanelGuest(EntityUid grid, NetEntity target)
    {
        if (!TryGetEntity(target, out var credential) || !TryComp<ShipGuestAccessComponent>(grid, out var guestAccess))
            return false;

        if (!(guestAccess.GuestIdCards.Remove(credential.Value) | guestAccess.GuestCyborgs.Remove(credential.Value)))
            return false;

        Dirty(grid, guestAccess);
        return true;
    }

    private bool GuestSelfGrantRestricted(EntityUid? grid)
    {
        return TryComp<ShipGuestAccessComponent>(grid, out var guests) && guests.OwnerGrantsOnly;
    }

    private bool SetGuestGrantRestriction(EntityUid grid, bool ownerOnly)
    {
        if (GuestSelfGrantRestricted(grid) == ownerOnly)
            return false;

        EnsureComp<ShipGuestAccessComponent>(grid).OwnerGrantsOnly = ownerOnly;
        return true;
    }

    private bool TrySetDoorProtection(EntityUid grid, NetEntity target, bool enabled)
    {
        if (!TryGetEntity(target, out var door) || !HasComp<DoorComponent>(door) ||
            !TryComp<ShipAccessReaderComponent>(door, out var reader) ||
            !TryComp<TransformComponent>(door, out var xform) || !xform.Anchored || xform.ParentUid != grid)
            return false;

        return SetShipReaderProtection(door.Value, reader, enabled);
    }

    private bool SetShipReaderProtection(EntityUid entity, ShipAccessReaderComponent reader, bool enabled)
    {
        if (reader.Enabled == enabled)
            return false;

        reader.Enabled = enabled;
        Dirty(entity, reader);
        return true;
    }
}
