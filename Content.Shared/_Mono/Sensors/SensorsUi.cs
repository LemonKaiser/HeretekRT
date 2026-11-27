using System.Linq;
using Robust.Shared.Serialization;

namespace Content.Shared._Mono.Sensors;

[Serializable, NetSerializable]
public enum SensorsUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class SensorsBuiState : BoundUserInterfaceState
{
    public List<SensorContactDto> Contacts = new();
    public List<SensorHardwareDto> Hardware = new();
    public List<ChaffInterferenceArcDto> ChaffArcs = new();
    public List<string> DatalinkCodes = new();
    public float GridWorldRotationDegrees;
}

[Serializable, NetSerializable]
public sealed class SensorContactDto
{
    public NetEntity Target;
    public string? Name;
    public float Range;
    public float BearingDegrees;
    public string Sources = string.Empty;
    public float Rcs;
    public float IrSignature;
    public float ActiveRadarWatts;
    public TimeSpan LastSeen;
    public bool IsDatalink;
    public bool IsIlluminator;
}

[Serializable, NetSerializable]
public sealed class SensorHardwareDto
{
    public NetEntity Entity;
    public string Name = string.Empty;
    public SensorType SensorType;
    public bool Enabled;
    public bool ActiveMode;
    public bool Powered;
    public float Sensitivity;
    public float ArcDegrees;
    public float ArcOffset;
    public float TransmitWatts;
    public bool ArcConfigurable;
    public bool GridRelativeArc;
}

[Serializable, NetSerializable]
public sealed class SensorsSetEmconMessage : BoundUserInterfaceMessage
{
    public NetEntity Sensor;
    public bool Enabled;
    public bool ActiveMode;
}

[Serializable, NetSerializable]
public sealed class SensorsSetArcMessage : BoundUserInterfaceMessage
{
    public NetEntity Sensor;
    public float ArcOffsetDegrees;
}

[Serializable, NetSerializable]
public sealed class SensorsSetDatalinkMessage : BoundUserInterfaceMessage
{
    private const int MaxCodes = 8;

    public List<string> Codes = new();

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static List<string> NormalizeCodes(IEnumerable<string> codes)
    {
        return codes.Select(NormalizeCode)
            .Where(static code => code.Length > 0)
            .Distinct()
            .Take(MaxCodes)
            .ToList();
    }
}

[Serializable, NetSerializable]
public sealed class SensorsToggleHardwareMessage : BoundUserInterfaceMessage
{
    public NetEntity Sensor;
    public bool? Enabled;
    public bool? ActiveMode;
}

[Serializable, NetSerializable]
public sealed class ChaffInterferenceArcDto
{
    public float CenterDegrees;
    public float WidthDegrees;
    public float Intensity;
}
