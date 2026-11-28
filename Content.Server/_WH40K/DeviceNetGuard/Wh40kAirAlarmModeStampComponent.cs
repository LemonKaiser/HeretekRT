namespace Content.Server._WH40K.DeviceNetGuard;

/// <summary>Tracks the newest mode stamp applied to an air alarm so linked alarms never echo modes infinitely.</summary>
[RegisterComponent]
public sealed partial class Wh40kAirAlarmModeStampComponent : Component
{
    public uint Tick;
    public string Origin = string.Empty;
}
