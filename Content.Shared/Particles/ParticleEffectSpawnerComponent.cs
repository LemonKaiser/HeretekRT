using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Particles;

/// <summary>
/// Declarative particle sample placed from the entity spawn menu.
/// It has no gameplay behaviour: the client creates the cosmetic emitter from this prototype data.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ParticleEffectSpawnerComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<ParticleEffectPrototype> Effect;

    /// <summary>
    /// Number of emitters for a burst sample. Continuous samples must keep this at one.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Count = 1;

    [DataField, AutoNetworkedField]
    public Color? Color;

    [DataField, AutoNetworkedField]
    public Angle? EmitAngle;

    [DataField, AutoNetworkedField]
    public Vector2? Velocity;

    [DataField, AutoNetworkedField]
    public float Intensity = 1f;

    [DataField, AutoNetworkedField]
    public int? Seed;

    /// <summary>
    /// Whether a continuous sample follows this marker when it is moved.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Attach = true;
}
