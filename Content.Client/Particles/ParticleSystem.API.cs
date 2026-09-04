using Content.Shared.Particles;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.Particles;

/// <summary>
/// API for <see cref="ParticleSystem"/>.
/// Use these methods to create and remove particle effects from other systems.
/// </summary>
public sealed partial class ParticleSystem
{
    /// <summary>
    /// Spawns a particle effect at the map position of <paramref name="entity"/>,
    /// optionally attaching it so it follows the entity.
    /// </summary>
    /// <param name="effectId">The prototype ID of the effect to spawn.</param>
    /// <param name="entity">The entity to spawn particles on.</param>
    /// <param name="colorOverride">Optional color tint.</param>
    /// <param name="attach">When <c>true</c> (default), the emitter follows <paramref name="entity"/>.</param>
    /// <returns>The <see cref="ActiveEmitter"/> handle, or <c>null</c> if the effect could not be spawned.</returns>
    public ActiveEmitter? CreateParticle(
        ProtoId<ParticleEffectPrototype> effectId,
        EntityUid entity,
        Color? colorOverride = null,
        bool attach = true)
    {
        var coords = _transform.GetMapCoordinates(entity);
        return SpawnEffect(effectId, coords, attach ? entity : null, colorOverride);
    }

    /// <summary>
    /// Spawns a particle effect at an entity using complete per-instance presentation parameters.
    /// </summary>
    public ActiveEmitter? CreateParticle(
        ProtoId<ParticleEffectPrototype> effectId,
        EntityUid entity,
        ParticleSpawnParameters parameters,
        bool attach = true)
    {
        if (Deleted(entity))
            return null;

        return SpawnEffect(effectId, _transform.GetMapCoordinates(entity), parameters, attach ? entity : null);
    }

    /// <summary>
    /// Spawns a particle effect at the given map coordinates.
    /// </summary>
    /// <param name="effectId">The prototype ID of the effect to spawn.</param>
    /// <param name="coords">World position to spawn at.</param>
    /// <param name="colorOverride">Optional color tint.</param>
    /// <returns>The <see cref="ActiveEmitter"/> handle, or <c>null</c> if the effect could not be spawned.</returns>
    public ActiveEmitter? CreateParticle(
        ProtoId<ParticleEffectPrototype> effectId,
        MapCoordinates coords,
        Color? colorOverride = null)
    {
        return SpawnEffect(effectId, coords, null, colorOverride);
    }

    /// <summary>
    /// Spawns a particle effect at exact map coordinates using complete per-instance presentation parameters.
    /// </summary>
    public ActiveEmitter? CreateParticle(
        ProtoId<ParticleEffectPrototype> effectId,
        MapCoordinates coords,
        ParticleSpawnParameters parameters)
    {
        return SpawnEffect(effectId, coords, parameters);
    }

    /// <summary>
    /// Spawns a one-shot particle effect. This is the client counterpart of a replicated particle burst and rejects
    /// continuous prototypes to keep feature state out of transient events.
    /// </summary>
    public int SpawnBurst(
        ProtoId<ParticleEffectPrototype> effectId,
        MapCoordinates coords,
        int count = 1,
        ParticleSpawnParameters? parameters = null,
        EntityUid? attachedEntity = null)
    {
        if (!_proto.Resolve(effectId, out var prototype) || !prototype.Burst ||
            !ParticleSpawnLimits.TryNormalize(parameters, out var normalized))
            return 0;

        count = ParticleSpawnLimits.ClampEmitterCount(prototype, count);
        var spawned = 0;
        for (var i = 0; i < count; i++)
        {
            var emitterParameters = normalized.Seed is { } seed
                ? normalized with { Seed = ParticleSpawnLimits.DeriveSeed(seed, i) }
                : normalized;

            if (SpawnEffect(effectId, coords, emitterParameters, attachedEntity) is not null)
                spawned++;
        }

        return spawned;
    }

    /// <summary>
    /// Resolves a visual surface material to a burst effect and spawns it locally.
    /// Use this only for a purely client-side visual or a predicted action; authoritative bursts go through the server.
    /// </summary>
    public int SpawnMaterial(
        ProtoId<ParticleEffectSetPrototype> effectSet,
        ParticleSurfaceMaterial material,
        MapCoordinates coords,
        int count = 1,
        ParticleSpawnParameters? parameters = null,
        EntityUid? attachedEntity = null)
    {
        return _proto.Resolve(effectSet, out var set) && set.TryGetEffect(material, out var effect)
            ? SpawnBurst(effect, coords, count, parameters, attachedEntity)
            : 0;
    }

    /// <summary>
    /// Stops and removes a particle emitter by its <see cref="ActiveEmitter"/> reference. Nullable.
    /// </summary>
    public void RemoveParticle(ActiveEmitter? emitter)
    {
        if (emitter != null)
            StopEffect(emitter);
    }

    /// <summary>
    /// Stops and removes a particle emitter by its numeric handle.
    /// </summary>
    public void RemoveParticle(uint handle)
    {
        StopEffect(handle);
    }
}
