using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Warmth;

/// <summary>
/// Tracks a mob's felt thermal state: ambient tile temperature, body feel, strain severity, wetness,
/// gauge position, trend, and proximity to radiant warmth.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class Wh40kWarmthComponent : Component
{
    /// <summary>Whether the server has sampled the environment at least once.</summary>
    [AutoNetworkedField]
    public bool HasReading;

    /// <summary>Air temperature on the body's tile in whole degrees Celsius, or null in vacuum.</summary>
    [DataField, AutoNetworkedField]
    public int? HereCelsius;

    /// <summary>The measured body surface temperature in whole degrees Celsius.</summary>
    [AutoNetworkedField]
    public int BodyCelsius;

    [DataField, AutoNetworkedField]
    public Wh40kBodyFeel Feel = Wh40kBodyFeel.Comfortable;

    /// <summary>Active thermal warning strain, if any.</summary>
    [DataField, AutoNetworkedField]
    public Wh40kWarmthStrain Strain = Wh40kWarmthStrain.None;

    /// <summary>Warning strength from 0 at warning threshold to 1 at harm threshold.</summary>
    [DataField, AutoNetworkedField]
    public float Severity;

    /// <summary>How soaked the body is, from 0 (dry) to 1 (drenched).</summary>
    [DataField, AutoNetworkedField]
    public float Wetness;

    /// <summary>Position between cold harm (0) and heat harm (100), quantized in 5% steps.</summary>
    [DataField, AutoNetworkedField]
    public byte Gauge = 50;

    /// <summary>Whether the body is currently warming, cooling, or steady.</summary>
    [DataField, AutoNetworkedField]
    public Wh40kWarmthTrend Trend;

    /// <summary>Whether an active radiant fire or heater reaches this body.</summary>
    [DataField, AutoNetworkedField]
    public bool NearWarmth;

    [ViewVariables]
    public float ColdShare;

    [ViewVariables]
    public float HeatShare;

    [ViewVariables]
    public float LastTemperature = float.NaN;

    [ViewVariables]
    public float TemperatureRate;

    [ViewVariables]
    public float WindChill = 1f;

    [ViewVariables]
    public Wh40kWarmthStrain PendingStrain;

    [ViewVariables]
    public TimeSpan PendingStrainSince;

    [ViewVariables]
    public TimeSpan NextSign;

    [ViewVariables]
    public TimeSpan NextVisibleSign;
}

[Serializable, NetSerializable]
public enum Wh40kBodyFeel : byte
{
    Freezing,
    Cold,
    Chilly,
    Comfortable,
    Warm,
    Hot,
    Overheating,
}

[Serializable, NetSerializable]
public enum Wh40kWarmthTrend : byte
{
    Steady,
    Warming,
    Cooling,
}

[Serializable, NetSerializable]
public enum Wh40kWarmthStrain : byte
{
    None,
    Cold,
    Hot,
}
