using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Behaviors;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Shrapnel;

/// <summary>
/// Destructible behavior that flings a ring of shrapnel projectiles outward when a machine or structure breaks.
/// </summary>
[DataDefinition]
public sealed partial class Wh40kShrapnelBehavior : IThresholdBehavior
{
    [DataField(required: true)]
    public EntProtoId Proto;

    [DataField]
    public int Count = 30;

    [DataField]
    public float SpawnRadius = 0.9f;

    [DataField]
    public float Speed = 20f;

    [DataField]
    public float MinJitter = 2f;

    [DataField]
    public float MaxJitter = 6f;

    public void Execute(EntityUid owner, DestructibleSystem system, EntityUid? cause = null)
    {
        system.EntityManager.System<Wh40kShrapnelSystem>().Fling(owner, this);
    }
}
