using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Shared.Alert;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared._WH40K.Augments;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Augments;

/// <summary>
/// Server-only battery use and status alert for installed augment power-cell slots.
/// </summary>
public sealed class AugmentPowerCellSystem : SharedAugmentPowerCellSystem
{
    private static readonly TimeSpan AlertUpdateDelay = TimeSpan.FromSeconds(2);

    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly SharedPopupSystem _popups = default!;

    private TimeSpan _nextAlertUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HasAugmentPowerCellSlotComponent, AugmentBatteryAlertEvent>(OnBatteryAlert);
    }

    private void OnBatteryAlert(Entity<HasAugmentPowerCellSlotComponent> ent, ref AugmentBatteryAlertEvent args)
    {
        if (GetBodyAugment(ent) is not { } augment || GetAugmentCell(augment) is not { } battery)
        {
            _popups.PopupEntity(Loc.GetString("power-cell-no-battery"), args.User, args.User, PopupType.MediumCaution);
            return;
        }

        var percent = 100f * battery.Comp.CurrentCharge / battery.Comp.MaxCharge;
        var draw = CompOrNull<PowerCellDrawComponent>(augment)?.DrawRate ?? 0f;
        _popups.PopupEntity(Loc.GetString("augments-power-cell-info", ("percent", $"{percent:F0}"), ("draw", draw)),
            args.User, args.User);
    }

    public Entity<BatteryComponent>? GetAugmentCell(EntityUid augment)
    {
        return _powerCell.TryGetBatteryFromSlot(augment, out var batteryEntity, out var battery) &&
               batteryEntity is { } entity
            ? (entity, battery)
            : null;
    }

    /// <summary>
    /// Uses an internal cell charge, displaying a precise reason to the wearer if it cannot be used.
    /// </summary>
    public bool TryUseChargeBody(EntityUid body, float amount)
    {
        if (GetBodyAugment(body) is not { } slot)
        {
            _popups.PopupEntity(Loc.GetString("augments-no-power-cell-slot"), body, body, PopupType.MediumCaution);
            return false;
        }

        if (GetAugmentCell(slot) is not { } battery)
        {
            _popups.PopupEntity(Loc.GetString("power-cell-no-battery"), body, body, PopupType.MediumCaution);
            return false;
        }

        if (_battery.TryUseCharge(battery.Owner, amount))
            return true;

        _popups.PopupEntity(Loc.GetString("power-cell-insufficient"), body, body, PopupType.MediumCaution);
        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextAlertUpdate)
            return;

        _nextAlertUpdate = _timing.CurTime + AlertUpdateDelay;
        var query = EntityQueryEnumerator<HasAugmentPowerCellSlotComponent>();
        while (query.MoveNext(out var body, out _))
        {
            if (_mobs.IsDead(body) || GetBodyAugment(body) is not { } augment)
                continue;

            UpdateBatteryAlert(body, augment);
        }
    }

    private void UpdateBatteryAlert(EntityUid body, Entity<AugmentPowerCellSlotComponent> augment)
    {
        if (GetAugmentCell(augment) is not { } battery)
        {
            _alerts.ClearAlert(body, augment.Comp.BatteryAlert);
            _alerts.ShowAlert(body, augment.Comp.NoBatteryAlert);
            return;
        }

        var chargePercent = (short) MathF.Round(battery.Comp.CurrentCharge / battery.Comp.MaxCharge * 10f);
        if (chargePercent == 0 && PowerCell.HasDrawCharge(augment.Owner))
            chargePercent = 1;

        _alerts.ClearAlert(body, augment.Comp.NoBatteryAlert);
        _alerts.ShowAlert(body, augment.Comp.BatteryAlert, chargePercent);
    }
}
