using Content.Server._Mono.Temperature.Components;
using Content.Server._Mono.Temperature.Systems;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._WH40K.Warmth;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared._WH40K.Weather;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Temperature;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Warmth;

/// <summary>
/// Tracks each mob's ambient air temperature, body thermal feel, rain/snow wetness, wind chill,
/// radiant heat proximity, clothing thermal ratings, and pre-harm shivering/sweating warnings.
/// </summary>
public sealed partial class Wh40kWarmthSystem : SharedWh40kWarmthSystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private EntityRadiusHeaterSystem _heaters = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedWolfWeatherSystem _wolfWeather = default!;
    [Dependency] private KoronusSafetyPolicySystem _safety = default!;

    private static readonly TimeSpan HeaterScanInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StrainConfirmation = TimeSpan.FromSeconds(3);

    private const float ShelterDrying = 2.5f;
    private const float FireDrying = 6f;
    private const float SkinChill = 10f;
    private const float SkinWarm = 6f;
    private const float SkinDamageMargin = 3f;
    private const float MildShare = 0.3f;
    private const float MildRecoveryScale = 0.85f;
    private const float StrainRecoveryMargin = 0.1f;
    private const float SevereShare = 0.8f;
    private const float VacuumMoles = 0.5f;
    private const float SteadyRate = 0.0005f;
    private const float RateSmoothing = 0.3f;

    private readonly HashSet<Entity<EntityRadiusHeaterComponent>> _fires = new();
    private readonly HashSet<EntityUid> _heaterMaps = new();
    private TimeSpan _nextHeaterScan;

    private float _windChillPerSpeed;
    private float _windChillMax;
    private float _wetCooling;
    private float _drySeconds;
    private float _warningShare;

    private static readonly string[] ColdSigns =
        { "wh40k-warmth-cold-sign-1", "wh40k-warmth-cold-sign-2", "wh40k-warmth-cold-sign-3" };

    private static readonly string[] HeatSigns =
        { "wh40k-warmth-heat-sign-1", "wh40k-warmth-heat-sign-2", "wh40k-warmth-heat-sign-3" };

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(Cfg, Wh40kWarmthCVars.WindChillPerSpeed, value => _windChillPerSpeed = value, true);
        Subs.CVar(Cfg, Wh40kWarmthCVars.WindChillMax, value => _windChillMax = value, true);
        Subs.CVar(Cfg, Wh40kWarmthCVars.WetCooling, value => _wetCooling = value, true);
        Subs.CVar(Cfg, Wh40kWarmthCVars.DrySeconds, value => _drySeconds = value, true);
        Subs.CVar(Cfg, Wh40kWarmthCVars.WarningShare, value => _warningShare = value, true);

        SubscribeLocalEvent<EntityRadiusHeaterComponent, ComponentStartup>(OnHeaterStartup);
        SubscribeLocalEvent<Wh40kWarmthComponent, ModifyChangedTemperatureEvent>(OnModifyTemperature);
        SubscribeLocalEvent<Wh40kWarmthComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<Wh40kWarmthComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<TemperatureProtectionComponent, ExaminedEvent>(OnClothingExamined);
    }

    private void OnHeaterStartup(Entity<EntityRadiusHeaterComponent> ent, ref ComponentStartup args)
    {
        _nextHeaterScan = TimeSpan.Zero;
    }

    private void OnPlayerAttached(EntityUid uid, Wh40kWarmthComponent comp, PlayerAttachedEvent args)
    {
        Dirty(uid, comp);
    }

    private void OnClothingExamined(Entity<TemperatureProtectionComponent> ent, ref ExaminedEvent args)
    {
        _temperature.PushTemperatureProtectionExamine(ent, args);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var period = Math.Max(1u, (uint) MathF.Round(_timing.TickRate));
        var seconds = period / (float) _timing.TickRate;
        var slot = _timing.CurTick.Value % period;

        var query = EntityQueryEnumerator<TemperatureComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var temperature, out _))
        {
            if (((uint) uid.Id % period) != slot)
                continue;

            Tick(uid, temperature, seconds);
        }
    }

    public void Tick(EntityUid body, TemperatureComponent temperature, float seconds)
    {
        var xform = Transform(body);
        if (xform.MapUid == null)
            return;

        var comp = EnsureComp<Wh40kWarmthComponent>(body);
        var dirty = !comp.HasReading;
        comp.HasReading = true;

        var air = _atmos.GetContainingMixture((body, xform));
        int? here = air is { } mix && mix.TotalMoles > VacuumMoles
            ? (int) MathF.Round(TemperatureHelpers.KelvinToCelsius(mix.Temperature))
            : null;
        if (comp.HereCelsius != here)
        {
            comp.HereCelsius = here;
            dirty = true;
        }

        var open = _wolfWeather.TryGetOpenExposure(body, xform, out var wetting, out var wind);
        comp.WindChill = open && wind > 0f
            ? MathF.Min(1f + wind * _windChillPerSpeed, MathF.Max(1f, _windChillMax))
            : 1f;

        var nearFire = NearFire(xform);
        dirty |= UpdateWetness(comp, open, nearFire, wetting, seconds);
        dirty |= UpdateFeel(body, comp, temperature);
        dirty |= UpdateGauge(body, comp, nearFire, seconds);

        if (dirty && HasComp<ActorComponent>(body))
            Dirty(body, comp);
    }

    private void OnModifyTemperature(EntityUid uid, Wh40kWarmthComponent comp, ModifyChangedTemperatureEvent args)
    {
        if (args.TemperatureDelta >= 0f)
            return;

        args.TemperatureDelta *= comp.WindChill * (1f + MathF.Max(0f, _wetCooling) * comp.Wetness);
    }

    private void OnExamined(Entity<Wh40kWarmthComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || ent.Comp.Wetness < 0.2f)
            return;

        var id = ent.Comp.Wetness >= 0.6f ? "wh40k-warmth-examine-soaked" : "wh40k-warmth-examine-damp";
        args.PushMarkup(Loc.GetString(id, ("target", Identity.Entity(ent, EntityManager))));
    }

    private bool UpdateWetness(Wh40kWarmthComponent comp, bool open, bool nearFire, float wetting, float seconds)
    {
        var before = comp.Wetness;
        if (wetting > 0f)
        {
            comp.Wetness = MathF.Min(1f, comp.Wetness + wetting * seconds);
        }
        else if (comp.Wetness > 0f)
        {
            var rate = _drySeconds > 0f ? 1f / _drySeconds : 1f;
            if (!open)
                rate *= ShelterDrying;
            if (nearFire)
                rate *= FireDrying;

            comp.Wetness = MathF.Max(0f, comp.Wetness - rate * seconds);
        }

        return Quantize(before) != Quantize(comp.Wetness);
    }

    public bool NearFire(TransformComponent xform)
    {
        if (xform.MapUid is not { } map || !MapHasHeater(map))
            return false;

        _fires.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, EntityRadiusHeaterSystem.MaxRadius, _fires);
        foreach (var fire in _fires)
        {
            if (!_heaters.IsActive(fire.Owner, fire.Comp))
                continue;

            if (xform.Coordinates.TryDistance(EntityManager, Transform(fire.Owner).Coordinates, out var distance)
                && distance < fire.Comp.Radius)
                return true;
        }

        return false;
    }

    private bool MapHasHeater(EntityUid map)
    {
        if (_timing.CurTime >= _nextHeaterScan)
        {
            _nextHeaterScan = _timing.CurTime + HeaterScanInterval;
            _heaterMaps.Clear();
            var heaters = EntityQueryEnumerator<EntityRadiusHeaterComponent, TransformComponent>();
            while (heaters.MoveNext(out _, out _, out var heaterXform))
            {
                if (heaterXform.MapUid is { } heaterMap)
                    _heaterMaps.Add(heaterMap);
            }
        }

        return _heaterMaps.Contains(map);
    }

    private bool UpdateFeel(EntityUid body, Wh40kWarmthComponent comp, TemperatureComponent temperature)
    {
        var coldLine = temperature.ParentColdDamageThreshold ?? temperature.ColdDamageThreshold;
        var heatLine = temperature.ParentHeatDamageThreshold ?? temperature.HeatDamageThreshold;
        var normal = TryComp(body, out ThermalRegulatorComponent? regulator)
                     && regulator.NormalBodyTemperature > coldLine
                     && regulator.NormalBodyTemperature < heatLine
            ? regulator.NormalBodyTemperature
            : (coldLine + heatLine) / 2f;

        var surface = temperature.CurrentTemperature;
        var cold = normal > coldLine ? MathF.Max(0f, (normal - surface) / (normal - coldLine)) : 0f;
        var heat = heatLine > normal ? MathF.Max(0f, (surface - normal) / (heatLine - normal)) : 0f;
        var warn = Math.Clamp(_warningShare, 0.05f, 0.99f);
        var safeFromHeat = (heat >= Math.Min(warn, SevereShare) || comp.Strain == Wh40kWarmthStrain.Hot) &&
            _safety.HasRule(body, KoronusSafetyRule.FireAndHighTemperature);

        if (surface <= coldLine + SkinDamageMargin)
            cold = MathF.Max(cold, warn);

        comp.ColdShare = cold;
        comp.HeatShare = heat;

        var chillScale = comp.Feel == Wh40kBodyFeel.Chilly ? MildRecoveryScale : 1f;
        var warmScale = comp.Feel == Wh40kBodyFeel.Warm ? MildRecoveryScale : 1f;
        var feel = Wh40kBodyFeel.Comfortable;
        if (cold >= SevereShare)
            feel = Wh40kBodyFeel.Freezing;
        else if (cold >= warn)
            feel = Wh40kBodyFeel.Cold;
        else if (!safeFromHeat && heat >= SevereShare)
            feel = Wh40kBodyFeel.Overheating;
        else if (!safeFromHeat && heat >= warn)
            feel = Wh40kBodyFeel.Hot;
        else if (cold >= MildShare * chillScale || surface < normal - SkinChill * chillScale)
            feel = Wh40kBodyFeel.Chilly;
        else if (heat >= MildShare * warmScale || surface > normal + SkinWarm * warmScale)
            feel = Wh40kBodyFeel.Warm;

        var candidate = Wh40kWarmthStrain.None;
        var harmful = false;
        if (!_mobState.IsDead(body))
        {
            var recovery = MathF.Max(0.01f, warn - StrainRecoveryMargin);
            if (cold >= (comp.Strain == Wh40kWarmthStrain.Cold ? recovery : warn))
            {
                candidate = Wh40kWarmthStrain.Cold;
                harmful = surface <= coldLine;
            }
            else if (!safeFromHeat && heat >= (comp.Strain == Wh40kWarmthStrain.Hot ? recovery : warn))
            {
                candidate = Wh40kWarmthStrain.Hot;
                harmful = surface >= heatLine;
            }
        }

        var strain = ConfirmStrain(comp, candidate, harmful);
        var severity = strain switch
        {
            Wh40kWarmthStrain.Cold => Math.Clamp((cold - warn) / (1f - warn), 0f, 1f),
            Wh40kWarmthStrain.Hot => Math.Clamp((heat - warn) / (1f - warn), 0f, 1f),
            _ => 0f,
        };

        var dirty = false;
        if (comp.Feel != feel)
        {
            comp.Feel = feel;
            dirty = true;
        }

        if (comp.Strain != strain)
        {
            comp.Strain = strain;
            dirty = true;
            Movement.RefreshMovementSpeedModifiers(body);
            if (strain != Wh40kWarmthStrain.None)
                FirstWarning(body, comp, strain);
        }

        if (Quantize(comp.Severity) != Quantize(severity))
            dirty = true;
        comp.Severity = severity;

        if (strain != Wh40kWarmthStrain.None)
            Signs(body, comp, strain);

        return dirty;
    }

    private Wh40kWarmthStrain ConfirmStrain(Wh40kWarmthComponent comp, Wh40kWarmthStrain candidate, bool harmful)
    {
        if (candidate == Wh40kWarmthStrain.None || candidate == comp.Strain || harmful)
        {
            comp.PendingStrain = Wh40kWarmthStrain.None;
            return candidate;
        }

        var now = _timing.CurTime;
        if (comp.PendingStrain != candidate)
        {
            comp.PendingStrain = candidate;
            comp.PendingStrainSince = now;
        }

        return now - comp.PendingStrainSince >= StrainConfirmation ? candidate : Wh40kWarmthStrain.None;
    }

    private bool UpdateGauge(EntityUid body, Wh40kWarmthComponent comp, bool nearFire, float seconds)
    {
        if (!HasComp<ActorComponent>(body))
            return false;

        var raw = Math.Clamp(0.5f + (comp.HeatShare - comp.ColdShare) * 0.5f, 0f, 1f);
        comp.GaugeRate = float.IsNaN(comp.LastGauge)
            ? 0f
            : comp.GaugeRate + ((raw - comp.LastGauge) / seconds - comp.GaugeRate) * RateSmoothing;
        comp.LastGauge = raw;

        var trend = comp.GaugeRate > SteadyRate
            ? Wh40kWarmthTrend.Warming
            : comp.GaugeRate < -SteadyRate ? Wh40kWarmthTrend.Cooling : Wh40kWarmthTrend.Steady;
        var gauge = (byte) (MathF.Round(raw * 20f) * 5f);

        if (comp.Gauge == gauge && comp.Trend == trend && comp.NearWarmth == nearFire)
            return false;

        comp.Gauge = gauge;
        comp.Trend = trend;
        comp.NearWarmth = nearFire;
        return true;
    }

    private void FirstWarning(EntityUid body, Wh40kWarmthComponent comp, Wh40kWarmthStrain strain)
    {
        if (!HasComp<ActorComponent>(body))
            return;

        var id = strain == Wh40kWarmthStrain.Cold ? "wh40k-warmth-cold-start" : "wh40k-warmth-heat-start";
        _popup.PopupEntity(Loc.GetString(id), body, body, PopupType.MediumCaution);
        comp.NextSign = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(15f, 25f));
        comp.NextVisibleSign = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(45f, 75f));
    }

    private void Signs(EntityUid body, Wh40kWarmthComponent comp, Wh40kWarmthStrain strain)
    {
        if (!HasComp<ActorComponent>(body))
            return;

        var now = _timing.CurTime;
        if (now >= comp.NextSign)
        {
            comp.NextSign = now + TimeSpan.FromSeconds(_random.NextFloat(20f, 35f));
            var id = strain == Wh40kWarmthStrain.Cold ? _random.Pick(ColdSigns) : _random.Pick(HeatSigns);
            _popup.PopupEntity(Loc.GetString(id), body, body, PopupType.SmallCaution);
        }

        if (now < comp.NextVisibleSign)
            return;

        comp.NextVisibleSign = now + TimeSpan.FromSeconds(_random.NextFloat(45f, 75f));
        var othersKey = strain == Wh40kWarmthStrain.Cold ? "wh40k-warmth-shiver-others" : "wh40k-warmth-sweat-others";
        _popup.PopupEntity(Loc.GetString(othersKey, ("target", Identity.Entity(body, EntityManager))),
            body, Filter.PvsExcept(body), true);
    }

    private static int Quantize(float share) => (int) MathF.Round(share * 20f);
}
