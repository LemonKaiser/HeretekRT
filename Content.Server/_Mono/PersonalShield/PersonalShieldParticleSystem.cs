using System.Numerics;
using Content.Server.Particles;
using Content.Shared._Mono.PersonalShield;
using Content.Shared.Particles;
using Robust.Shared.Maths;

namespace Content.Server._Mono.PersonalShield;

/// <summary>
/// Replicates the short shield-hit response only after PersonalShield has spent charge.
/// </summary>
public sealed class PersonalShieldParticleSystem : EntitySystem
{
    [Dependency] private readonly ParticleSpawnSystem _particles = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PersonalShieldComponent, PersonalShieldAbsorbedEvent>(OnShieldAbsorbed);
    }

    private void OnShieldAbsorbed(Entity<PersonalShieldComponent> shield, ref PersonalShieldAbsorbedEvent args)
    {
        var coordinates = _transform.GetMapCoordinates(shield);
        Angle? angle = null;
        if (args.Origin is { } origin)
        {
            var source = _transform.GetMapCoordinates(origin);
            var direction = coordinates.Position - source.Position;
            if (source.MapId == coordinates.MapId && direction.LengthSquared() > 0.0001f)
                angle = Angle.FromWorldVec(direction);
        }

        _particles.Spawn(
            coordinates,
            "HrtShieldHit",
            parameters: new ParticleSpawnParameters(
                EmitAngle: angle,
                Intensity: Math.Clamp(0.45f + args.Amount / 30f, 0.45f, 1f)),
            attachedEntity: shield.Owner,
            rateLimitSource: shield.Owner,
            cooldown: TimeSpan.FromMilliseconds(140));
    }
}
