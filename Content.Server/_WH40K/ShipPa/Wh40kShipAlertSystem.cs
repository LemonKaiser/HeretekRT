using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._WH40K.ShipPa;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Robust.Server.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.ShipPa;

/// <summary>
/// Handles helm console requests for Ship Vox-PA situation codes, General Quarters klaxon,
/// volume, crew announcements, and SOS distress calls.
/// </summary>
public sealed partial class Wh40kShipAlertSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private Wh40kShipPaSystem _pa = default!;

    public const string GeneralQuartersAlarm = "general-quarters";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShuttleConsoleComponent, BeforeActivatableUIOpenEvent>(OnConsoleBeforeOpen);

        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<Wh40kShipAlertCodeRequestMessage>(OnCodeRequest);
            subs.Event<Wh40kShipGeneralQuartersRequestMessage>(OnGeneralQuartersRequest);
            subs.Event<Wh40kShipPaAnnounceRequestMessage>(OnAnnounceRequest);
            subs.Event<Wh40kShipDistressRequestMessage>(OnDistressRequest);
            subs.Event<Wh40kShipPaVolumeRequestMessage>(OnVolumeRequest);
        });
    }

    private bool IsAdmin(EntityUid actor)
    {
        return _players.TryGetSessionByEntity(actor, out var session) && _admin.IsAdmin(session);
    }

    private void OnConsoleBeforeOpen(Entity<ShuttleConsoleComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        if (GetConsoleGrid(ent) is not { } grid)
            return;

        EnsureComp<Wh40kShipAlertComponent>(grid);
        _pa.RefreshCounts(grid);
    }

    private void OnVolumeRequest(Entity<ShuttleConsoleComponent> ent, ref Wh40kShipPaVolumeRequestMessage args)
    {
        if (GetConsoleGrid(ent) is not { } grid || !float.IsFinite(args.Volume))
            return;

        var alert = EnsureComp<Wh40kShipAlertComponent>(grid);
        var volume = Math.Clamp(args.Volume, Wh40kShipAlertComponent.MinVolume, 1f);
        if (MathHelper.CloseTo(alert.Volume, volume))
            return;

        alert.Volume = volume;
        Dirty(grid, alert);
        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.Actor):player} set the Vox-PA volume of {ToPrettyString(grid):grid} to {volume:P0}");
    }

    private void OnCodeRequest(Entity<ShuttleConsoleComponent> ent, ref Wh40kShipAlertCodeRequestMessage args)
    {
        if (GetConsoleGrid(ent) is not { } grid)
            return;

        if (!_proto.TryIndex<Wh40kShipAlertCodePrototype>(args.Code, out var proto) || !proto.Selectable)
            return;

        SetCode(grid, args.Code, args.Actor);
        WarnIfUnheard(ent, grid, args.Actor);
    }

    private void OnGeneralQuartersRequest(Entity<ShuttleConsoleComponent> ent, ref Wh40kShipGeneralQuartersRequestMessage args)
    {
        if (GetConsoleGrid(ent) is not { } grid)
            return;

        SetGeneralQuarters(grid, args.Active, args.Actor);
        WarnIfUnheard(ent, grid, args.Actor);
    }

    private void OnAnnounceRequest(Entity<ShuttleConsoleComponent> ent, ref Wh40kShipPaAnnounceRequestMessage args)
    {
        if (GetConsoleGrid(ent) is not { } grid)
            return;

        var alert = EnsureComp<Wh40kShipAlertComponent>(grid);
        var text = Sanitize(args.Text, alert.MaxAnnouncementLength);

        if (string.IsNullOrWhiteSpace(text))
            return;

        if (_timing.CurTime < alert.NextAnnouncement && !IsAdmin(args.Actor))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-ship-pa-announce-cooldown"), ent, args.Actor);
            return;
        }

        var sender = Loc.GetString("wh40k-ship-pa-sender", ("ship", _pa.GetShipName(grid)));

        if (!_pa.Announce(grid, text, alert.AnnouncementChime, sender))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-ship-pa-announce-no-speakers"), ent, args.Actor);
            return;
        }

        alert.NextAnnouncement = _timing.CurTime + alert.AnnouncementCooldown;

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} announced on the Vox-PA of {ToPrettyString(grid):grid}: {text}");
    }

    public void SetCode(EntityUid grid, ProtoId<Wh40kShipAlertCodePrototype> code, EntityUid? user = null, bool announce = true)
    {
        if (!Exists(grid) || !_proto.TryIndex(code, out var proto))
            return;

        if (user != null && !proto.Selectable)
            return;

        var alert = EnsureComp<Wh40kShipAlertComponent>(grid);
        if (alert.Code == code)
            return;

        var old = alert.Code;
        alert.Code = code;
        Dirty(grid, alert);

        var ev = new Wh40kShipAlertCodeChangedEvent(grid, old, code, user);
        RaiseLocalEvent(grid, ref ev, broadcast: true);

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{(user is { } actor ? ToPrettyString(actor).ToString() : "Server")} set {ToPrettyString(grid)} to {code.Id}");

        if (announce)
            _pa.Announce(grid, Loc.GetString(proto.Announcement, ("ship", _pa.GetShipName(grid))), proto.Sound, color: proto.Color);
    }

    public void SetGeneralQuarters(EntityUid grid, bool active, EntityUid? user = null)
    {
        if (!Exists(grid))
            return;

        var alert = EnsureComp<Wh40kShipAlertComponent>(grid);
        if (alert.GeneralQuarters == active)
            return;

        alert.GeneralQuarters = active;
        Dirty(grid, alert);

        if (active)
        {
            var message = Loc.GetString(alert.GeneralQuartersAnnouncement);
            _pa.StartAlarm(grid, GeneralQuartersAlarm, alert.GeneralQuartersAlarm, message: message, color: alert.GeneralQuartersColor);
            _pa.Announce(grid, message, alert.GeneralQuartersSound ?? alert.AnnouncementChime, color: alert.GeneralQuartersColor);
        }
        else
        {
            _pa.StopAlarm(grid, GeneralQuartersAlarm);
            _pa.Announce(
                grid,
                Loc.GetString(alert.GeneralQuartersSecureAnnouncement),
                alert.GeneralQuartersSecureSound ?? alert.AnnouncementChime);
        }

        var ev = new Wh40kShipGeneralQuartersChangedEvent(grid, active, user);
        RaiseLocalEvent(grid, ref ev, broadcast: true);

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{(user is { } actor ? ToPrettyString(actor).ToString() : "Server")} {(active ? "sounded" : "secured from")} general quarters on {ToPrettyString(grid)}");
    }

    private EntityUid? GetConsoleGrid(EntityUid console)
    {
        var ev = new ConsoleShuttleEvent { Console = console };
        RaiseLocalEvent(console, ref ev);

        if (ev.Console is not { } target || !TryComp(target, out TransformComponent? xform))
            return null;

        return xform.GridUid;
    }

    private void WarnIfUnheard(EntityUid console, EntityUid grid, EntityUid actor)
    {
        if (_pa.CountSpeakers(grid).Online == 0)
            _popup.PopupEntity(Loc.GetString("wh40k-ship-pa-announce-no-speakers"), console, actor);
    }

    private static string Sanitize(string text, int maxLength)
    {
        return SharedChatSystem.SanitizeAnnouncement(text, maxLength)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}
