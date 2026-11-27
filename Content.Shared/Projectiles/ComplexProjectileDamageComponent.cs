using Content.Shared.Damage;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared.Projectiles;

/// <summary>
/// Overrides <see cref="ProjectileComponent.Damage"/> per target using the first matching whitelist/blacklist entry.
/// Falls back to <see cref="ProjectileComponent.Damage"/> when no option matches.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ComplexProjectileDamageComponent : Component
{
    /// <summary>
    /// Ordered list of target-specific damage overrides.
    /// </summary>
    [DataField]
    public DamageOption[] DamageOptions = [];
}

[DataDefinition]
public partial struct DamageOption
{
    [DataField]
    public DamageSpecifier Damage;

    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField]
    public EntityWhitelist? Blacklist;
}
