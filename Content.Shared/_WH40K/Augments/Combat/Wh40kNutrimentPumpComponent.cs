using Content.Shared.Nutrition.Components;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Internal nutrient micro-pump that feeds an organic host whenever hunger falls to Starving.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class Wh40kNutrimentPumpComponent : Component
{
    [DataField]
    public HungerThreshold Threshold = HungerThreshold.Starving;

    [DataField]
    public float Amount = 15f;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(5);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextCheck;
}
