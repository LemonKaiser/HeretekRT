using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Server.Silicons.Borgs;

public sealed partial class BorgSystem
{
    /// <summary>
    /// Refreshes the cyborg's BUI only while it is open and only when the displayed whole charge
    /// percentage or battery presence has changed, avoiding per-tick UI broadcasts when the headlight runs.
    /// </summary>
    private void Wh40kUpdateChargeUi(EntityUid uid, BorgChassisComponent component)
    {
        if (!_ui.IsUiOpen(uid, BorgUiKey.Key))
            return;

        var hasBattery = false;
        var percent = 0;
        if (_powerCell.TryGetBatteryFromSlot(uid, out var battery))
        {
            hasBattery = true;
            percent = Wh40kShownPercent(battery.CurrentCharge / battery.MaxCharge);
        }

        if (_ui.TryGetUiState<BorgBuiState>(uid, BorgUiKey.Key, out var shown)
            && shown.HasBattery == hasBattery
            && Wh40kShownPercent(shown.ChargePercent) == percent)
            return;

        UpdateUI(uid, component);
    }

    private static int Wh40kShownPercent(float fraction)
    {
        return (int) MathF.Round(fraction * 100);
    }
}
