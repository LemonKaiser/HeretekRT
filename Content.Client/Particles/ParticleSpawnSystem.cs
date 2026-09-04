using Content.Shared.Particles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.Particles;

/// <summary>
/// Creates cosmetic particle effects requested by the server through <see cref="SpawnParticlesEvent"/>.
/// </summary>
public sealed class ParticleSpawnSystem : EntitySystem
{
    [Dependency] private readonly ParticleSystem _particles = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeNetworkEvent<SpawnParticlesEvent>(OnSpawnParticles);
    }

    private void OnSpawnParticles(SpawnParticlesEvent ev)
    {
        var coordinates = GetCoordinates(ev.Coordinates);
        if (ev.Count <= 0 || !_proto.Resolve(ev.Effect, out var prototype) ||
            !coordinates.IsValid(EntityManager) ||
            !ParticleSpawnLimits.TryNormalize(ev.Parameters, out var parameters))
            return;

        var mapCoordinates = _transform.ToMapCoordinates(coordinates);
        if (mapCoordinates.MapId == MapId.Nullspace)
            return;

        EntityUid? attachedEntity = null;
        if (ev.AttachedEntity is { } attached)
        {
            if (!TryGetEntity(attached, out var entity) || Deleted(entity.Value))
                return;

            attachedEntity = entity.Value;
        }

        var count = ParticleSpawnLimits.ClampEmitterCount(prototype, ev.Count);
        _particles.SpawnBurst(ev.Effect, mapCoordinates, count, parameters, attachedEntity);
    }
}
