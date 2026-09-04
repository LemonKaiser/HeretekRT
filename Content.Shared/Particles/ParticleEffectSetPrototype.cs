using Robust.Shared.Prototypes;

namespace Content.Shared.Particles;

/// <summary>
/// Material category used to select a presentation effect for a surface interaction.
/// The categories are visual only; gameplay systems remain responsible for their own material logic.
/// </summary>
public enum ParticleSurfaceMaterial : byte
{
    Default,
    Flesh,
    Metal,
    Stone,
    Concrete,
    Glass,
    Wood,
    Plastic,
    Ice,
    Liquid,
    Energy,
    Shield,
}

/// <summary>
/// Maps a surface material to a particle effect and falls back to <see cref="ParticleSurfaceMaterial.Default"/>.
/// </summary>
[Prototype]
public sealed partial class ParticleEffectSetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public Dictionary<ParticleSurfaceMaterial, ProtoId<ParticleEffectPrototype>> Effects { get; private set; } = new();

    public bool TryGetEffect(ParticleSurfaceMaterial material, out ProtoId<ParticleEffectPrototype> effect)
    {
        return Effects.TryGetValue(material, out effect) ||
               Effects.TryGetValue(ParticleSurfaceMaterial.Default, out effect);
    }
}
