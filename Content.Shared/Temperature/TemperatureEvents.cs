using Content.Shared.Inventory;

namespace Content.Shared.Temperature;

public sealed class ModifyChangedTemperatureEvent : EntityEventArgs, IInventoryRelayEvent
{
    public SlotFlags TargetSlots { get; } = ~SlotFlags.POCKET;

    /// <summary>Requested heat transfer in joules, after thermal protection modifiers.</summary>
    public float TemperatureDelta;

    /// <summary>Ambient temperature in kelvin when the transfer comes from the environment.</summary>
    public readonly float? AmbientTemperature;

    /// <summary>Maximum positive transfer in joules, applied after all modifiers.</summary>
    public float MaximumHeating = float.PositiveInfinity;

    public ModifyChangedTemperatureEvent(float temperature, float? ambientTemperature = null)
    {
        TemperatureDelta = temperature;
        AmbientTemperature = ambientTemperature;
    }
}

public sealed class OnTemperatureChangeEvent : EntityEventArgs
{
    public readonly float CurrentTemperature;
    public readonly float LastTemperature;
    public readonly float TemperatureDelta;

    public OnTemperatureChangeEvent(float current, float last, float delta)
    {
        CurrentTemperature = current;
        LastTemperature = last;
        TemperatureDelta = delta;
    }
}

