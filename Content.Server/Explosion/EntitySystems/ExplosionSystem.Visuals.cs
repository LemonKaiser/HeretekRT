using System.Numerics;
using Content.Shared.Explosion;
using Content.Shared.Explosion.Components;
using Content.Shared.Explosion.EntitySystems;
using Content.Shared.Particles;
using Robust.Server.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Server.Explosion.EntitySystems;

// This part of the system handled send visual / overlay data to clients.
public sealed partial class ExplosionSystem
{
    public void InitVisuals()
    {
        SubscribeLocalEvent<ExplosionVisualsComponent, ComponentGetState>(OnGetState);
    }

    private void OnGetState(EntityUid uid, ExplosionVisualsComponent component, ref ComponentGetState args)
    {
        Dictionary<NetEntity, Dictionary<int, List<Vector2i>>> tileLists = new();
        foreach (var (grid, data) in component.Tiles)
        {
            tileLists.Add(GetNetEntity(grid), data);
        }

        args.State = new ExplosionVisualsState(
            component.Epicenter,
            component.ExplosionType,
            component.Intensity,
            component.SpaceTiles,
            tileLists,
            component.SpaceMatrix,
            component.SpaceTileSize);
    }

    /// <summary>
    ///     Constructor for the shared <see cref="ExplosionEvent"/> using the server-exclusive explosion classes.
    /// </summary>
    private EntityUid CreateExplosionVisualEntity(MapCoordinates epicenter, string prototype, Matrix3x2 spaceMatrix, ExplosionSpaceTileFlood? spaceData, IEnumerable<ExplosionGridTileFlood> gridData, List<float> iterationIntensity)
    {
        var explosionEntity = Spawn(null, MapCoordinates.Nullspace);
        var comp = AddComp<ExplosionVisualsComponent>(explosionEntity);

        foreach (var grid in gridData)
        {
            comp.Tiles.Add(grid.Grid.Owner, grid.TileLists);
        }

        comp.SpaceTiles = spaceData?.TileLists;
        comp.Epicenter = epicenter;
        comp.ExplosionType = prototype;
        comp.Intensity = iterationIntensity;
        comp.SpaceMatrix = spaceMatrix;
        comp.SpaceTileSize = spaceData?.TileSize ?? DefaultTileSize;
        Dirty(explosionEntity, comp);

        // Light, sound & visuals may extend well beyond normal PVS range. In principle, this should probably still be
        // restricted to something like the same map, but whatever.
        _pvsSys.AddGlobalOverride(explosionEntity);

        var appearance = AddComp<AppearanceComponent>(explosionEntity);
        _appearance.SetData(explosionEntity, ExplosionAppearanceData.Progress, 1, appearance);

        SpawnParticleVisuals(epicenter, explosionEntity, iterationIntensity.Count);

        return explosionEntity;
    }

    /// <summary>
    /// Adds a bounded cosmetic layer to an explosion that has already completed its server-side tile calculation.
    /// Damage, fire, debris entities and the existing explosion overlay remain authoritative systems of their own.
    /// </summary>
    private void SpawnParticleVisuals(MapCoordinates epicenter, EntityUid source, int iterationCount)
    {
        // Iterations correlate with the resolved explosion radius, but this is presentation only and stays inside a
        // small safe range. The five effects together have a maximum of 70 live particles before quality reduction.
        var intensity = Math.Clamp(0.65f + iterationCount * 0.035f, 0.65f, 1.2f);
        var parameters = new ParticleSpawnParameters(Intensity: intensity);

        _particles.Spawn(epicenter, "HrtExplosionFlash", parameters: parameters, rateLimitSource: source);
        _particles.Spawn(epicenter, "HrtExplosionFireball", parameters: parameters, rateLimitSource: source);
        _particles.Spawn(epicenter, "HrtExplosionDebris", parameters: parameters, rateLimitSource: source);
        _particles.Spawn(epicenter, "HrtExplosionSmoke", parameters: parameters, rateLimitSource: source);
        _particles.Spawn(epicenter, "HrtDustLight", parameters: parameters, rateLimitSource: source);
    }
}
