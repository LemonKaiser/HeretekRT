using Content.Shared._WH40K.Bosses;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WH40K.Bosses;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class Wh40kChaosMarineTestComponent : Component
{
    /// <summary>Null retains the full director; None is a target or movement-only test.</summary>
    [DataField] public MarineAttack? Attack;
    [DataField] public bool Invulnerable = true;
    [DataField] public bool Movement = true;
    [DataField] public int CompletedWaves = 7;
    [DataField] public int SummonCount = 2;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextCycle;
}

/// <summary>A stationary additional target recognized only by isolated pattern tests.</summary>
[RegisterComponent]
public sealed partial class Wh40kChaosMarineTestTargetComponent : Component;
