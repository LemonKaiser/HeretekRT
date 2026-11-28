using Content.Shared.Movement.Systems;
using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.Warmth;

/// <summary>
/// Shared thermal strain slowdown and presentation helpers for body warmth.
/// </summary>
public abstract partial class SharedWh40kWarmthSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    protected IConfigurationManager Cfg => _cfg;
    protected MovementSpeedModifierSystem Movement => _movement;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kWarmthComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(Entity<Wh40kWarmthComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var modifier = ent.Comp.Strain switch
        {
            Wh40kWarmthStrain.Cold => _cfg.GetCVar(Wh40kWarmthCVars.ColdSlowdown),
            Wh40kWarmthStrain.Hot => _cfg.GetCVar(Wh40kWarmthCVars.HeatSlowdown),
            _ => 1f,
        };

        if (modifier < 1f)
            args.ModifySpeed(modifier, modifier);
    }

    public static string FeelLoc(Wh40kBodyFeel feel) => feel switch
    {
        Wh40kBodyFeel.Freezing => "wh40k-warmth-feel-freezing",
        Wh40kBodyFeel.Cold => "wh40k-warmth-feel-cold",
        Wh40kBodyFeel.Chilly => "wh40k-warmth-feel-chilly",
        Wh40kBodyFeel.Warm => "wh40k-warmth-feel-warm",
        Wh40kBodyFeel.Hot => "wh40k-warmth-feel-hot",
        Wh40kBodyFeel.Overheating => "wh40k-warmth-feel-overheating",
        _ => "wh40k-warmth-feel-comfortable",
    };

    public static Color FeelColor(Wh40kBodyFeel feel) => feel switch
    {
        Wh40kBodyFeel.Freezing => Color.FromHex("#6BB5FF"),
        Wh40kBodyFeel.Cold => Color.FromHex("#7CCAF2"),
        Wh40kBodyFeel.Chilly => Color.FromHex("#A5D4D0"),
        Wh40kBodyFeel.Warm => Color.FromHex("#E6C47A"),
        Wh40kBodyFeel.Hot => Color.FromHex("#E89242"),
        Wh40kBodyFeel.Overheating => Color.FromHex("#E0534A"),
        _ => Color.FromHex("#88C98F"),
    };

    public static string HereText(Wh40kWarmthComponent comp) => comp.HereCelsius is { } celsius
        ? Robust.Shared.Localization.Loc.GetString("wh40k-warmth-here", ("celsius", celsius))
        : Robust.Shared.Localization.Loc.GetString("wh40k-warmth-here-vacuum");
}
