using System;
using Content.Server._WH40K.SectorMap.Systems;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Server-only runtime data for one authored Koronus system map.
/// </summary>
[RegisterComponent, Access(typeof(KoronusSectorRuleSystem), typeof(KoronusSectorResidencySystem), typeof(KoronusSectorBoundarySystem), typeof(KoronusPlanetarySystem))]
public sealed partial class KoronusSystemMapComponent : Component
{
    [ViewVariables]
    public string SystemId = string.Empty;

    [ViewVariables]
    public int IncomingSectorJumps;

    [ViewVariables]
    public TimeSpan? EmptySince;

    /// <summary>
    /// Prevents a failed serialization from being retried every residency tick.
    /// </summary>
    [ViewVariables]
    public TimeSpan? ColdUnloadRetryAt;
}
