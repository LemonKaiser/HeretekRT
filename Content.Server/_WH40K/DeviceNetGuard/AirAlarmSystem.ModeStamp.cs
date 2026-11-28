using Content.Server._WH40K.DeviceNetGuard;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Robust.Shared.Timing;

namespace Content.Server.Atmos.Monitor.Systems;

public sealed partial class AirAlarmSystem
{
    [Dependency] private IGameTiming _stampTiming = default!;

    private const string ModeStampTick = "wh40k_air_alarm_mode_tick";
    private const string ModeStampOrigin = "wh40k_air_alarm_mode_origin";

    private void StampLocalMode(EntityUid uid, string origin)
    {
        if (!TryComp<DeviceNetworkComponent>(uid, out var net) || net.Address != origin)
            return;

        var stamp = EnsureComp<Wh40kAirAlarmModeStampComponent>(uid);
        stamp.Tick = _stampTiming.CurTick.Value;
        stamp.Origin = origin;
    }

    private void WriteModeStamp(EntityUid uid, NetworkPayload payload)
    {
        if (!TryComp<Wh40kAirAlarmModeStampComponent>(uid, out var stamp))
            return;

        payload[ModeStampTick] = stamp.Tick;
        payload[ModeStampOrigin] = stamp.Origin;
    }

    private bool TryAcceptModeStamp(EntityUid uid, NetworkPayload payload)
    {
        if (!payload.TryGetValue(ModeStampTick, out uint tick) || !payload.TryGetValue(ModeStampOrigin, out string? origin))
            return false;

        var stamp = EnsureComp<Wh40kAirAlarmModeStampComponent>(uid);
        if (tick < stamp.Tick || tick == stamp.Tick && string.CompareOrdinal(origin, stamp.Origin) <= 0)
            return false;

        stamp.Tick = tick;
        stamp.Origin = origin;
        return true;
    }
}
