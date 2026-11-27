using Content.Shared.Power.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Periodically refills its owner's <see cref="BallisticAmmoProviderComponent"/> up to capacity,
/// analogous to <see cref="BatterySelfRechargerComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true), AutoGenerateComponentPause,
 Access(typeof(SharedGunSystem))]
public sealed partial class BallisticAmmoSelfRefillerComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool AutoRefill = true;

    [DataField, AutoNetworkedField]
    public TimeSpan AutoRefillRate = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public bool FiringPausesAutoRefill = false;

    [DataField, AutoNetworkedField]
    public TimeSpan AutoRefillPauseDuration = TimeSpan.Zero;

    [DataField, AutoNetworkedField]
    public EntProtoId? AmmoProto;

    [DataField, AutoNetworkedField]
    public bool AffectedByEmp = false;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextAutoRefill = TimeSpan.Zero;

    [DataField, AutoNetworkedField]
    public SoundSpecifier? RechargeSound;
}
