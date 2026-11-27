using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Synaptic reflex booster: enhances ship piloting (+15% thrust & turning) and provides a 5% chance to evade
/// incoming projectiles, at the cost of +5% incoming damage from over-sensitized nerves.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kReflexBoosterComponent : Component
{
    [DataField]
    public float ShipHandling = 1.15f;

    [DataField]
    public float DodgeChance = 0.05f;

    [DataField]
    public float DamageVulnerabilityMultiplier = 1.05f;
}
