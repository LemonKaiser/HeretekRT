using Content.Shared.RCD;
using Content.Shared.RCD.Components;

namespace Content.Server.Particles;

/// <summary>
/// Adds small, authoritative cosmetic feedback to completed engineering actions.
/// Each burst stays local to the action's PVS and is never emitted for a cancelled do-after.
/// </summary>
public sealed class EngineeringParticleSystem : EntitySystem
{
    [Dependency] private readonly ParticleSpawnSystem _particles = default!;
    [Dependency] private readonly MaterialParticleSystem _materialParticles = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RCDComponent, RCDOperationCompletedEvent>(OnRcdOperationCompleted);
    }

    private void OnRcdOperationCompleted(Entity<RCDComponent> ent, ref RCDOperationCompletedEvent args)
    {
        if (ent.Comp.IsRpd)
            return;

        if (args.Mode == RcdMode.Deconstruct && args.Target is { } target)
        {
            _materialParticles.SpawnDebris(target, args.Coordinates, 0.95f);
            return;
        }

        var effect = args.Mode == RcdMode.Deconstruct ? "HrtDustHeavy" : "HrtDustLight";
        _particles.Spawn(args.Coordinates, effect, rateLimitSource: ent.Owner, cooldown: TimeSpan.FromMilliseconds(100));
    }
}
