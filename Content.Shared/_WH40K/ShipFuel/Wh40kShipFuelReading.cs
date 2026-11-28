using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ShipFuel;

[Serializable, NetSerializable]
public readonly record struct Wh40kShipFuelReading(float Fraction, int Sources, int UnknownSources)
{
    public const float LowThreshold = 0.20f;
    public const float CriticalThreshold = 0.10f;

    public bool Available => Sources > 0 && UnknownSources == 0;
    public bool Low => Available && Fraction <= LowThreshold;
    public bool Critical => Available && Fraction < CriticalThreshold;
}

[Serializable, NetSerializable]
public sealed class Wh40kShipFuelMessage(Wh40kShipFuelReading reading) : BoundUserInterfaceMessage
{
    public readonly Wh40kShipFuelReading Reading = reading;
}
