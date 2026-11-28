using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Shared._WH40K.Warmth;
using Content.Shared.Atmos;
using Content.Shared.Examine;
using Content.Shared.Movement.Systems;
using Content.Shared.Temperature;

namespace Content.Server.Temperature.Systems;

public sealed partial class TemperatureSystem
{
    private const float WarmthNakedMargin = 21f;
    private const float WarmthWarningSkin = 300f;
    private const float WarmthDeepColdCelsius = -100f;

    /// <summary>Returns the effective damage limits and a comfortable temperature within them.</summary>
    public (float Cold, float Normal, float Hot) GetThermalRange(EntityUid uid, TemperatureComponent temperature)
    {
        var cold = temperature.ParentColdDamageThreshold ?? temperature.ColdDamageThreshold;
        var hot = temperature.ParentHeatDamageThreshold ?? temperature.HeatDamageThreshold;
        var normal = TryComp<ThermalRegulatorComponent>(uid, out var regulator)
            ? regulator.NormalBodyTemperature
            : Atmospherics.T20C;

        if (normal <= cold || normal >= hot)
            normal = (cold + hot) / 2f;

        return (cold, normal, hot);
    }

    public void ExchangeHeatWithEnvironment(EntityUid uid, TemperatureComponent temperature,
        float ambientTemperature, float ambientHeatCapacity, float transferScale = 1f)
    {
        var bodyCapacity = GetHeatCapacity(uid, temperature);
        var delta = ambientTemperature - temperature.CurrentTemperature;
        var heat = delta * (ambientHeatCapacity * bodyCapacity / (ambientHeatCapacity + bodyCapacity));
        ChangeHeat(uid, heat * temperature.AtmosTemperatureTransferEfficiency * transferScale,
            false, temperature, ambientTemperature);
    }

    private void OnTemperatureShutdown(Entity<TemperatureComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        _alerts.ClearAlertCategory(ent, TemperatureAlertCategory);
        if (!TryComp<Wh40kWarmthComponent>(ent, out var warmth))
            return;

        var hadStrain = warmth.Strain != Wh40kWarmthStrain.None;
        RemComp<Wh40kWarmthComponent>(ent);
        if (hadStrain)
            EntityManager.System<MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(ent);
    }

    public static float CalculateWarmToCelsius(float coolingCoefficient)
    {
        var share = MathF.Max(coolingCoefficient, 0.0001f);
        return TemperatureHelpers.KelvinToCelsius(WarmthWarningSkin - WarmthNakedMargin / share);
    }

    public void PushTemperatureProtectionExamine(Entity<TemperatureProtectionComponent> ent, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var cooling = ent.Comp.CoolingCoefficient;
        if (cooling < 0.95f)
        {
            var celsius = CalculateWarmToCelsius(cooling);
            args.PushMarkup(celsius <= WarmthDeepColdCelsius
                ? Loc.GetString("wh40k-warmth-clothing-deep-cold")
                : Loc.GetString("wh40k-warmth-clothing-warm-to", ("celsius", (int) (MathF.Round(celsius / 5f) * 5f))));
        }

        var heating = ent.Comp.HeatingCoefficient;
        if (heating <= 0.5f)
            args.PushMarkup(Loc.GetString("wh40k-warmth-clothing-heat-shield"));
        else if (heating > 1f)
            args.PushMarkup(Loc.GetString("wh40k-warmth-clothing-heat-trap"));
    }
}
