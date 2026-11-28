using System.Numerics;
using Content.Server.Radio.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._WH40K.ShipPa;
using Content.Shared.Database;
using Content.Shared.Radio.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Server._WH40K.ShipPa;

public sealed partial class Wh40kShipAlertSystem
{
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private void OnDistressRequest(Entity<ShuttleConsoleComponent> ent, ref Wh40kShipDistressRequestMessage args)
    {
        if (GetConsoleGrid(ent) is not { } grid)
            return;

        var alert = EnsureComp<Wh40kShipAlertComponent>(grid);
        if (args.Active && !alert.Distress && _timing.CurTime < alert.NextDistress && !IsAdmin(args.Actor))
        {
            var seconds = (int) Math.Ceiling((alert.NextDistress - _timing.CurTime).TotalSeconds);
            _popup.PopupEntity(Loc.GetString("wh40k-ship-distress-cooldown", ("seconds", seconds)), ent, args.Actor);
            return;
        }

        SetDistress(grid, args.Active, args.Actor);
    }

    public void SetDistress(EntityUid grid, bool active, EntityUid? user = null)
    {
        if (!Exists(grid))
            return;

        var alert = EnsureComp<Wh40kShipAlertComponent>(grid);
        if (alert.Distress == active)
            return;

        alert.Distress = active;
        Dirty(grid, alert);

        var ship = _pa.GetShipName(grid);
        if (active)
        {
            alert.DistressEnds = _timing.CurTime + alert.DistressDuration;
            alert.NextDistress = _timing.CurTime + alert.DistressCooldown;
            var position = GetShipPosition(grid).Position;
            BroadcastDistress(grid, alert, Loc.GetString("wh40k-ship-distress-radio",
                ("ship", ship),
                ("x", (int) MathF.Round(position.X)),
                ("y", (int) MathF.Round(position.Y))));
            _pa.Announce(grid, Loc.GetString("wh40k-ship-distress-announcement"), alert.AnnouncementChime, color: alert.DistressColor);
        }
        else
        {
            BroadcastDistress(grid, alert, Loc.GetString("wh40k-ship-distress-radio-cancel", ("ship", ship)));
            _pa.Announce(grid, Loc.GetString("wh40k-ship-distress-announcement-cancel"), alert.AnnouncementChime);
        }

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{(user is { } actor ? ToPrettyString(actor).ToString() : "Server")} {(active ? "sent" : "cancelled")} a distress call from {ToPrettyString(grid)}");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<Wh40kShipAlertComponent>();
        while (query.MoveNext(out var uid, out var alert))
        {
            if (!alert.Distress || _timing.CurTime < alert.DistressEnds)
                continue;

            alert.Distress = false;
            Dirty(uid, alert);
            _pa.Announce(uid, Loc.GetString("wh40k-ship-distress-announcement-lapsed"), alert.AnnouncementChime);
            _adminLogger.Add(LogType.Action, LogImpact.Low, $"The distress call of {ToPrettyString(uid)} lapsed");
        }
    }

    private void BroadcastDistress(EntityUid grid, Wh40kShipAlertComponent alert, string text)
    {
        var voice = Spawn(null, GetShipPosition(grid));
        _meta.SetEntityName(voice, _pa.GetShipName(grid));
        EnsureComp<TelecomExemptComponent>(voice);
        _radio.SendRadioMessage(voice, text, alert.DistressChannel, voice);
        QueueDel(voice);
    }

    private MapCoordinates GetShipPosition(EntityUid grid)
    {
        var centre = CompOrNull<PhysicsComponent>(grid)?.LocalCenter ?? Vector2.Zero;
        return _transform.ToMapCoordinates(new EntityCoordinates(grid, centre));
    }
}
