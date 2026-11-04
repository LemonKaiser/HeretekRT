using Content.Server.Atmos.EntitySystems;
using Content.Server._ES.TileFires;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Server.Chemistry.TileReactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class FlammableTileReaction : ITileReaction
    {
        [DataField("temperatureMultiplier")] private float _temperatureMultiplier = 1.15f;

        public FixedPoint2 TileReact(TileRef tile,
            ReagentPrototype reagent,
            FixedPoint2 reactVolume,
            IEntityManager entityManager,
            List<ReagentData>? data)
        {
            if (reactVolume <= FixedPoint2.Zero || tile.Tile.IsEmpty)
                return FixedPoint2.Zero;

            var atmosphereSystem = entityManager.System<AtmosphereSystem>();

            var environment = atmosphereSystem.GetTileMixture(tile.GridUid, null, tile.GridIndices, true);
            if (environment == null || (!atmosphereSystem.IsHotspotActive(tile.GridUid, tile.GridIndices) &&
                !entityManager.System<ESTileFireSystem>().TryGetTileFire(tile.GridUid, tile.GridIndices, out _)))
                return FixedPoint2.Zero;

            environment.Temperature *= MathF.Max(_temperatureMultiplier * reactVolume.Float(), 1f);
            atmosphereSystem.ReactTile(tile.GridUid, tile.GridIndices);

            return reactVolume;
        }
    }
}
