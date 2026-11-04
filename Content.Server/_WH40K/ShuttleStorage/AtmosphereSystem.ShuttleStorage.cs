using Content.Server.Atmos.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    public bool HasUnsafeShuttleAtmosphere(EntityUid grid)
        => TryComp(grid, out GridAtmosphereComponent? atmos) && atmos.HotspotTiles.Count != 0;
}
