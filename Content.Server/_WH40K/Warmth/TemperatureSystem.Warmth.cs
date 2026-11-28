using Content.Server.Temperature.Components;
using Content.Shared.Examine;
using Content.Shared.Temperature;

namespace Content.Server.Temperature.Systems;

public sealed partial class TemperatureSystem
{
    private const float WarmthNakedMargin = 21f;
    private const float WarmthWarningSkin = 300f;
    private const float WarmthDeepColdCelsius = -100f;

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
