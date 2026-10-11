using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._WH40K.Npcs;

/// <summary>Permanent hub protection; independent of temporary dialogue leases.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kNpcProtectionComponent : Component
{
    [DataField] public float HomeRadius = 3f;
    [DataField] public bool PreserveDamage;
    [ViewVariables] public EntityCoordinates? Home;
    [DataField] public DamageSpecifier? PreservedDamage;
}

[RegisterComponent]
public sealed partial class Wh40kNpcSpawnComponent : Component
{
    [DataField(required: true)] public string Prototype = string.Empty;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kNpcGearComponent : Component { }
