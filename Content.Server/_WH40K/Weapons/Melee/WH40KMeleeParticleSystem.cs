using System.Numerics;
using Content.Server.Particles;
using Content.Shared._WH40K.Weapons.Melee;
using Content.Shared.Particles;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.Weapons.Melee;

/// <summary>
/// Emits restrained contact effects for WH40K melee weapons only after their
/// authoritative melee damage has been accepted.
/// </summary>
public sealed partial class WH40KMeleeParticleSystem : EntitySystem
{
    [Dependency] private ParticleSpawnSystem _particles = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPowerFistComponent, MeleeDamageAppliedEvent>(OnPowerFistDamageApplied);
        SubscribeLocalEvent<WH40KChainswordComponent, MeleeDamageAppliedEvent>(OnChainswordDamageApplied);
    }

    private void OnPowerFistDamageApplied(Entity<WH40KPowerFistComponent> fist, ref MeleeDamageAppliedEvent args)
    {
        _particles.Spawn(
            args.ImpactCoordinates,
            "HrtEmpSparks",
            parameters: new ParticleSpawnParameters(
                EmitAngle: GetImpactAngle(args.User, args.ImpactCoordinates),
                Intensity: 0.8f),
            rateLimitSource: fist.Owner,
            cooldown: TimeSpan.FromMilliseconds(220));
    }

    private void OnChainswordDamageApplied(Entity<WH40KChainswordComponent> chainsword, ref MeleeDamageAppliedEvent args)
    {
        _particles.Spawn(
            args.ImpactCoordinates,
            "HrtWeldingBurst",
            parameters: new ParticleSpawnParameters(
                EmitAngle: GetImpactAngle(args.User, args.ImpactCoordinates),
                Intensity: 0.5f),
            rateLimitSource: chainsword.Owner,
            cooldown: TimeSpan.FromMilliseconds(120));
    }

    private Angle GetImpactAngle(EntityUid user, MapCoordinates impact)
    {
        var origin = _transform.GetMapCoordinates(user);
        if (origin.MapId != impact.MapId)
            return Angle.Zero;

        var direction = origin.Position - impact.Position;
        return direction.LengthSquared() > 0.0001f ? Angle.FromWorldVec(direction) : Angle.Zero;
    }
}
