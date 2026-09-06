using Content.Shared.Particles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Particles;

/// <summary>
/// Validates and sends cosmetic particle bursts only to clients in the source coordinates' PVS.
/// </summary>
public sealed class ParticleSpawnSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _mapManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<(EntityUid Source, ProtoId<ParticleEffectPrototype> Effect), TimeSpan> _nextAllowed = new();
    private readonly List<(EntityUid Source, ProtoId<ParticleEffectPrototype> Effect)> _expiredRateLimits = new();
    private TimeSpan _nextRateLimitCleanup;

    /// <summary>
    /// Spawns a burst at an entity's current map position. Set <paramref name="attached"/> for effects that should
    /// follow the entity for their short lifetime.
    /// </summary>
    public bool Spawn(
        EntityUid target,
        ProtoId<ParticleEffectPrototype> effect,
        int count = 1,
        ParticleSpawnParameters? parameters = null,
        bool attached = false,
        TimeSpan cooldown = default)
    {
        if (!Exists(target))
            return false;

        return Spawn(
            _transform.GetMapCoordinates(target),
            effect,
            count,
            parameters,
            attached ? target : null,
            target,
            cooldown);
    }

    /// <summary>
    /// Spawns a burst at an exact map coordinate. The optional attachment and rate-limit source are independent so
    /// a projectile impact can be positioned precisely while still being throttled by its shooter or projectile.
    /// </summary>
    public bool Spawn(
        MapCoordinates coordinates,
        ProtoId<ParticleEffectPrototype> effect,
        int count = 1,
        ParticleSpawnParameters? parameters = null,
        EntityUid? attachedEntity = null,
        EntityUid? rateLimitSource = null,
        TimeSpan cooldown = default)
    {
        if (coordinates.MapId == MapId.Nullspace || !_mapManager.MapExists(coordinates.MapId) || count <= 0 ||
            !_proto.Resolve(effect, out var prototype) ||
            !ParticleSpawnLimits.TryNormalize(parameters, out var normalized))
            return false;

        count = ParticleSpawnLimits.ClampEmitterCount(prototype, count);
        if (count == 0)
            return false;

        var now = _timing.CurTime;
        if (rateLimitSource is { } source && cooldown > TimeSpan.Zero)
        {
            var key = (source, effect);
            if (_nextAllowed.TryGetValue(key, out var nextAllowed) && now < nextAllowed)
                return false;

            _nextAllowed[key] = now + cooldown;
        }

        if (attachedEntity is { } attached && !Exists(attached))
            attachedEntity = null;

        normalized = normalized with { Seed = normalized.Seed ?? _random.Next() };
        var entityCoordinates = _transform.ToCoordinates(coordinates);

        RaiseNetworkEvent(
            new SpawnParticlesEvent(
                GetNetCoordinates(entityCoordinates),
                attachedEntity is { } entity ? GetNetEntity(entity) : null,
                effect,
                count,
                normalized),
            Filter.Pvs(entityCoordinates, entityMan: EntityManager));
        return true;
    }

    /// <summary>
    /// Resolves an effect from a visual material set and spawns it with the same safety guarantees as <see cref="Spawn(MapCoordinates,ProtoId{ParticleEffectPrototype},int,ParticleSpawnParameters?,EntityUid?,EntityUid?,TimeSpan)"/>.
    /// </summary>
    public bool SpawnMaterial(
        MapCoordinates coordinates,
        ProtoId<ParticleEffectSetPrototype> effectSet,
        ParticleSurfaceMaterial material,
        int count = 1,
        ParticleSpawnParameters? parameters = null,
        EntityUid? attachedEntity = null,
        EntityUid? rateLimitSource = null,
        TimeSpan cooldown = default)
    {
        return _proto.Resolve(effectSet, out var set) && set.TryGetEffect(material, out var effect) &&
               Spawn(coordinates, effect, count, parameters, attachedEntity, rateLimitSource, cooldown);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _nextRateLimitCleanup)
            return;

        _nextRateLimitCleanup = now + TimeSpan.FromMinutes(1);
        _expiredRateLimits.Clear();
        foreach (var (key, nextAllowed) in _nextAllowed)
        {
            if (nextAllowed <= now || !Exists(key.Source))
                _expiredRateLimits.Add(key);
        }

        foreach (var key in _expiredRateLimits)
        {
            _nextAllowed.Remove(key);
        }
    }
}
