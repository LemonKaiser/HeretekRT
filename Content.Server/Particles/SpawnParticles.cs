using System.Numerics;
using Content.Shared.EntityEffects;
using Content.Shared.Particles;
using Robust.Shared.Prototypes;

namespace Content.Server.Particles;

/// <summary>
/// Entity effect that requests a cosmetic particle prototype at the target entity.
/// </summary>
public sealed partial class SpawnParticles : EntityEffect
{
    [DataField(required: true)]
    public ProtoId<ParticleEffectPrototype> ParticleProto;

    [DataField]
    public bool Attached;

    [DataField]
    public int Number = 1;

    [DataField]
    public Color? Color;

    [DataField]
    public Angle? EmitAngle;

    [DataField]
    public Vector2? Velocity;

    [DataField]
    public float Intensity = 1f;

    [DataField]
    public int? Seed;

    [DataField]
    public TimeSpan Cooldown;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var scale = args is EntityEffectReagentArgs reagentArgs
            ? MathF.Floor(reagentArgs.Scale.Float())
            : 1f;
        if (!float.IsFinite(scale) || scale <= 0f || Number <= 0)
            return;

        var count = (int) Math.Min((double) Number * scale, int.MaxValue);
        var parameters = new ParticleSpawnParameters(Color, EmitAngle, Velocity, Intensity, Seed);

        args.EntityManager.EntitySysManager.GetEntitySystem<ParticleSpawnSystem>()
            .Spawn(args.TargetEntity, ParticleProto, count, parameters, Attached, Cooldown);
    }
}
