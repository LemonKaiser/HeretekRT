using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WH40K.GroundCover;
using Content.Shared._WH40K.Weather;
using Content.Shared.Atmos;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.GroundCover;

/// <summary>
/// Wets open ground in rain and dries it after, and lays snow cover in snow weather that melts in warm air, worked
/// only near players and sent to them by chunk.
/// </summary>
public sealed partial class Wh40kGroundCoverSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public const float Freezing = Atmospherics.T0C;

    /// <summary>Cover levels a minute that melt just above freezing; warmer air melts faster.</summary>
    public const float MeltPerMinute = 0.5f;

    /// <summary>Rain below this wets the ground but fills no puddles.</summary>
    public const float PuddleRain = 0.3f;

    /// <summary>Seconds the puddles' tint takes to ease most of the way to the rain now falling.</summary>
    private const float TintSeconds = 20f;

    private static readonly TimeSpan MapInterval = TimeSpan.FromSeconds(1);

    private readonly List<EntityUid> _newMaps = new();
    private TimeSpan _nextMapUpdate;

    public override void Initialize()
    {
        base.Initialize();
        InitializeNetwork();
        InitializeShovel();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ShutdownNetwork();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now >= _nextMapUpdate)
        {
            _nextMapUpdate = now + MapInterval;
            UpdateMaps();
        }

        UpdateSnow(now);
        UpdateHeat(now);
        UpdateDecals(now);
        UpdateNetwork(now);
    }

    private void UpdateMaps()
    {
        _newMaps.Clear();
        var maps = EntityQueryEnumerator<WolfWeatherMapComponent>();
        while (maps.MoveNext(out var uid, out var weather))
        {
            if (weather.Weathers.Count > 0 && !HasComp<Wh40kGroundWeatherComponent>(uid)
                && _wolfWeather.IsSurface(uid) && CanAccumulate(weather))
                _newMaps.Add(uid);
        }

        foreach (var uid in _newMaps)
        {
            EnsureComp<Wh40kGroundWeatherComponent>(uid);
        }

        var grounds = EntityQueryEnumerator<Wh40kGroundWeatherComponent>();
        while (grounds.MoveNext(out var uid, out var ground))
        {
            StepMap((uid, ground), (float) MapInterval.TotalSeconds);
        }
    }

    private bool CanAccumulate(WolfWeatherMapComponent weather)
    {
        foreach (var entry in weather.Weathers)
        {
            if (_proto.TryIndex(entry.Weather, out var proto) && (proto.GroundWetness > 0f || proto.Snowfall > 0f))
                return true;
        }

        return false;
    }

    /// <summary>Rain, snowfall and rain tint weighted by each weather's current strength.</summary>
    private (float Rain, float Snow, Color? Tint) GetPrecipitation(EntityUid map)
    {
        if (!_wolfWeather.IsSurface(map) || !TryComp<WolfWeatherMapComponent>(map, out var weather))
            return (0f, 0f, null);

        var now = _wolfWeather.GetWeatherTime(map);
        var rain = 0f;
        var snow = 0f;
        var tintSum = Vector3.Zero;
        foreach (var entry in weather.Weathers)
        {
            if (!_proto.TryIndex(entry.Weather, out var proto))
                continue;

            var strength = SharedWolfWeatherSystem.GetStrength(entry, proto, now);
            var wetness = strength * proto.GroundWetness;
            rain += wetness;
            snow += strength * proto.Snowfall;

            var tint = proto.GroundTint ?? Wh40kGroundWeatherComponent.Water;
            tintSum += new Vector3(tint.R, tint.G, tint.B) * wetness;
        }

        var rainTint = rain > 0.001f ? new Color(tintSum.X / rain, tintSum.Y / rain, tintSum.Z / rain) : (Color?) null;
        return (Math.Clamp(rain, 0f, 1f), MathF.Max(snow, 0f), rainTint);
    }

    /// <summary>Moves a map's wetness, puddles and snow totals on by some seconds of its current weather.</summary>
    public void StepMap(Entity<Wh40kGroundWeatherComponent> map, float seconds)
    {
        var comp = map.Comp;
        (comp.Rain, comp.Snow, var rainTint) = GetPrecipitation(map);

        comp.WetnessExact = Approach(comp.WetnessExact, comp.Rain, comp.WetRate * seconds, comp.DryRate * seconds);
        var puddles = Math.Clamp((comp.Rain - PuddleRain) / (1f - PuddleRain), 0f, 1f);
        comp.PuddlesExact = Approach(comp.PuddlesExact, puddles, comp.PuddleRate * seconds, comp.PuddleDryRate * seconds);

        if (comp.Snow > 0f)
        {
            var fallen = comp.Snow * seconds / 60f;
            comp.Fallen += fallen;
            comp.BaseDepth = MathF.Min(Wh40kSnowCover.MaxLevels, comp.BaseDepth + fallen);
        }
        else
        {
            comp.MeltClock += seconds;
            if (comp.BaseDepth > 0f && MapTemperature(map) is { } temperature && temperature > Freezing)
                comp.BaseDepth = MathF.Max(0f, comp.BaseDepth - MeltRate(temperature) * seconds / 60f);
        }

        // The puddles keep the colour of the last rain while they dry.
        if (rainTint is { } target)
        {
            var ease = MathF.Min(1f, seconds / TintSeconds);
            comp.TintExact = Color.InterpolateBetween(comp.TintExact, target, ease);
        }

        // Clients get wetness in fiftieths and the tint in thirty-seconds, so a slow change doesn't send the map every second.
        var wetness = MathF.Round(comp.WetnessExact * 50f) / 50f;
        var shown = MathF.Round(comp.PuddlesExact * 50f) / 50f;
        var tint = new Color(MathF.Round(comp.TintExact.R * 32f) / 32f, MathF.Round(comp.TintExact.G * 32f) / 32f,
            MathF.Round(comp.TintExact.B * 32f) / 32f);
        if (wetness == comp.Wetness && shown == comp.Puddles && tint == comp.Tint)
            return;

        comp.Wetness = wetness;
        comp.Puddles = shown;
        comp.Tint = tint;
        Dirty(map);
    }

    /// <summary>How wet a tile is: the map's wetness on open ground, none under cover.</summary>
    public float GetTileWetness(EntityUid grid, Vector2i tile)
    {
        if (!TryComp(grid, out TransformComponent? xform)
            || !TryComp<Wh40kGroundWeatherComponent>(xform.MapUid, out var ground)
            || !IsOpen(grid, tile))
            return 0f;

        return ground.WetnessExact;
    }

    /// <summary>Cover levels a minute melting in air of a temperature.</summary>
    public static float MeltRate(float temperature)
    {
        return MeltPerMinute * Math.Clamp((temperature - Freezing) / 10f, 0.25f, 2f);
    }

    private float? MapTemperature(EntityUid map)
    {
        return _atmos.GetTileMixture(null, map, Vector2i.Zero)?.Temperature;
    }

    private static float Approach(float value, float target, float up, float down)
    {
        return value < target ? MathF.Min(target, value + up) : MathF.Max(target, value - down);
    }
}
