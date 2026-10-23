using System.Numerics;
using Content.Server.Light.EntitySystems;
using Content.Server._WH40K.SectorMap.Components;
using Content.Shared.Light.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.SectorMap.Systems;

public sealed partial class KoronusPlanetarySystem
{
    [Dependency] private LightCycleSystem _lightCycle = default!;

    private void ConfigureSurfaceLighting(EntityUid map, KoronusPlanetSurfaceMapComponent runtime)
    {
        if (!_prototypes.TryIndex<KoronusPlanetSurfacePrototype>(runtime.SurfaceId, out var surface) ||
            surface.Planet is not { } planetId)
            return;

        var planet = _prototypes.Index(planetId);
        if (TryComp<LightCycleComponent>(map, out var cycle))
        {
            // Capture the authoritative daylight after map initialization, never a client's
            // already dimmed ambient color. Empty surfaces must not remain frozen at night.
            cycle.OriginalColor = planet.MapLight;
            cycle.PauseWithMap = false;
            cycle.InitialOffset = false;
            cycle.MinLightLevel = 0.12f;
            cycle.MaxLightLevel = 1f;
            cycle.ClipLight = 1f;
            cycle.MinLevel = Color.FromHex("#7388B5");
            cycle.MaxLevel = Color.White;
            cycle.ClipLevel = Color.White;
            if (cycle.Enabled && cycle.DayDuration > TimeSpan.Zero)
            {
                _lightCycle.SetOffset((map, cycle), cycle.DayDuration / 2);
                if (TryComp<SunShadowCycleComponent>(map, out var shadows))
                {
                    var day = (float) (cycle.DayDuration.TotalSeconds / cycle.Duration.TotalSeconds);
                    shadows.Duration = cycle.Duration;
                    shadows.Directions =
                    [
                        new() { Ratio = 0f, Direction = new Vector2(0f, 3f), Alpha = 0f },
                        new() { Ratio = day * 0.25f, Direction = new Vector2(-3f, -0.1f), Alpha = 0.5f },
                        new() { Ratio = day * 0.5f, Direction = new Vector2(0f, -3f), Alpha = 0.8f },
                        new() { Ratio = day * 0.75f, Direction = new Vector2(3f, -0.1f), Alpha = 0.5f },
                        new() { Ratio = day, Direction = new Vector2(0f, 3f), Alpha = 0f },
                    ];
                    Dirty(map, shadows);
                }
            }
            Dirty(map, cycle);
        }

        foreach (var grid in runtime.AuthoredBaseGrids)
        {
            // A closed wall or rock perimeter says nothing about the ceiling. Surface maps
            // explicitly mark their actual roofs with RoofComponent, including enclosed yards.
            // ImplicitRoof from a space-station template would shadow the entire terrain grid.
            RemComp<ImplicitRoofComponent>(grid);
            EnsureComp<RoofComponent>(grid);
        }
    }
}
