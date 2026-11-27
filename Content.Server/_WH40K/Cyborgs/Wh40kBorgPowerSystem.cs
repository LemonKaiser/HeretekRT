using Content.Server.PowerCell;
using Content.Shared._WH40K.Augments;
using Content.Shared.Examine;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Server._WH40K.Cyborgs;

/// <summary>
/// Connects cyborg chassis to <see cref="GetDrinkerBatteryEvent"/> so they can drain APCs and loose cells,
/// and displays the installed power cell name on examine.
/// </summary>
public sealed partial class Wh40kBorgPowerSystem : EntitySystem
{
    [Dependency] private PowerCellSystem _powerCell = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgChassisComponent, GetDrinkerBatteryEvent>(OnGetDrinkerBattery);
        SubscribeLocalEvent<BorgChassisComponent, ExaminedEvent>(OnExamined);
    }

    private void OnGetDrinkerBattery(Entity<BorgChassisComponent> ent, ref GetDrinkerBatteryEvent args)
    {
        if (args.Battery != null)
            return;

        if (_powerCell.TryGetBatteryFromSlot(ent.Owner, out var cell, out var battery))
            args.Battery = (cell.Value, battery);
    }

    private void OnExamined(Entity<BorgChassisComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (_powerCell.TryGetBatteryFromSlot(ent.Owner, out var cell, out _))
            args.PushMarkup(Loc.GetString("wh40k-borg-examine-cell", ("cell", cell.Value)));
    }
}
