using Content.Shared.Light.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;

namespace Content.Shared._WH40K.Weather;

public abstract partial class SharedWolfWeatherSystem
{
    [Dependency] private SharedContainerSystem _warmthContainer = default!;

    private const float RainSoakSeconds = 60f;
    private const float SnowSoakSeconds = 120f;

    /// <summary>
    /// Checks whether a body stands exposed to active WolfWeather and returns wetting rate (per second) and wind speed.
    /// </summary>
    public bool TryGetOpenExposure(EntityUid body, TransformComponent xform, out float wetting, out float wind)
    {
        wetting = 0f;
        wind = 0f;

        if (xform.MapUid is not { } mapUid
            || !TryComp(mapUid, out WolfWeatherMapComponent? weather)
            || _warmthContainer.IsEntityOrParentInContainer(body)
            || xform.GridUid is not { } gridUid
            || !TryComp(gridUid, out MapGridComponent? grid))
        {
            return false;
        }

        TryComp(gridUid, out RoofComponent? roof);
        if (!IsExposed((gridUid, grid, roof), _map.GetTileRef(gridUid, grid, xform.Coordinates)))
            return false;

        var now = GetWeatherTime(mapUid);
        foreach (var entry in weather.Weathers)
        {
            if (!Proto.TryIndex(entry.Weather, out var proto))
                continue;

            var strength = GetStrength(entry, proto, now);
            var soak = MathF.Max(proto.GroundWetness / RainSoakSeconds, MathF.Min(proto.Snowfall, 1f) / SnowSoakSeconds);
            wetting = MathF.Max(wetting, soak * strength);
        }

        if (GetCurrent(weather) is { } current && Proto.TryIndex(current.Weather, out var currentProto))
            wind = weather.Wind.Length() * GetStrength(current, currentProto, now);

        return true;
    }
}
