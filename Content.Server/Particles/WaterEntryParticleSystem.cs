using System.Linq;
using Content.Shared.Particles;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server.Particles;

/// <summary>
/// Emits a water splash when an entity crosses into a water tile, rather than once per footstep.
/// </summary>
public sealed partial class WaterEntryParticleSystem : EntitySystem
{
    [Dependency] private ParticleSpawnSystem _particles = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _inWater = [];
    private readonly Dictionary<EntityUid, GameTick> _pendingWaterExits = [];

    public override void Initialize()
    {
        SubscribeLocalEvent<WaterEntryParticleComponent, StepTriggeredOffEvent>(OnWaterEntered);
        SubscribeLocalEvent<WaterEntryParticleComponent, EndCollideEvent>(OnWaterExited);
    }

    private void OnWaterEntered(Entity<WaterEntryParticleComponent> ent, ref StepTriggeredOffEvent args)
    {
        if (!_inWater.Add(args.Tripper))
            return;

        _pendingWaterExits.Remove(args.Tripper);
        _particles.Spawn(
            _transform.GetMapCoordinates(args.Tripper),
            "HrtWaterSplash",
            parameters: new ParticleSpawnParameters(Intensity: 1f),
            rateLimitSource: args.Tripper,
            cooldown: TimeSpan.FromMilliseconds(600));
    }

    private void OnWaterExited(Entity<WaterEntryParticleComponent> ent, ref EndCollideEvent args)
    {
        // Check after the next physics update: at a seam between two water tiles, the next contact may not yet
        // have been registered when this callback runs.
        _pendingWaterExits[args.OtherEntity] = _timing.CurTick + 2;
    }

    public override void Update(float frameTime)
    {
        foreach (var (entity, checkTick) in _pendingWaterExits.ToArray())
        {
            if (_timing.CurTick < checkTick)
                continue;

            _pendingWaterExits.Remove(entity);
            if (!Exists(entity) || !IsTouchingWater(entity))
                _inWater.Remove(entity);
        }
    }

    private bool IsTouchingWater(EntityUid entity)
    {
        foreach (var contact in _physics.GetContactingEntities(entity))
        {
            if (HasComp<WaterEntryParticleComponent>(contact))
                return true;
        }

        return false;
    }
}
