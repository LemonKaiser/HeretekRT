using Content.Shared._WH40K.SectorMap.Prototypes;

namespace Content.Shared._WH40K.SectorMap;

/// <summary>
/// Shared, data-only admission policy for the permanent RT asteroid fields.
/// Activity eligibility is deliberately reused as the explicit list of unprotected external
/// systems; no field can be configured for Footfall by accident.
/// </summary>
public static class KoronusAsteroidFieldPolicy
{
    public const int MaximumAsteroidsPerSystem = 8;
    public const float MinimumInnerRadius = 100f;

    public static bool IsEligible(
        string systemId,
        bool enabled,
        bool activityEligible,
        KoronusAsteroidFieldDefinition? field)
    {
        return systemId != "Footfall" &&
               enabled &&
               activityEligible &&
               field != null &&
               field.Count is > 0 and <= MaximumAsteroidsPerSystem &&
               field.InnerRadius >= MinimumInnerRadius &&
               field.OuterRadius >= field.InnerRadius;
    }
}
