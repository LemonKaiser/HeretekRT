using System.Linq;
using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Light.Components;

/// <summary>
/// Applies <see cref="SunShadowComponent"/> direction vectors based on a time-offset. Will track <see cref="LightCycleComponent"/> on on MapInit
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class SunShadowCycleDirection
{
    [DataField]
    public float Ratio;

    [DataField]
    public Vector2 Direction;

    [DataField]
    public float Alpha;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SunShadowCycleComponent : Component
{
    /// <summary>
    /// How long an entire cycle lasts
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan Duration = TimeSpan.FromMinutes(30);

    [DataField, AutoNetworkedField]
    public TimeSpan Offset;

    // Originally had this as ratios but it was slightly annoying to use.

    /// <summary>
    /// Time to have each direction applied. Will lerp from the current value to the next one.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<SunShadowCycleDirection> Directions = new()
    {
        new() { Ratio = 0f, Direction = new Vector2(0f, 3f), Alpha = 0f },
        new() { Ratio = 0.25f, Direction = new Vector2(-3f, -0.1f), Alpha = 0.5f },
        new() { Ratio = 0.5f, Direction = new Vector2(0f, -3f), Alpha = 0.8f },
        new() { Ratio = 0.75f, Direction = new Vector2(3f, -0.1f), Alpha = 0.5f },
    };
}
