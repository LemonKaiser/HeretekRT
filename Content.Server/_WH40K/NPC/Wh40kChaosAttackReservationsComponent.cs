using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WH40K.NPC;

[RegisterComponent, AutoGenerateComponentPause]
/// <summary>Short reservations on a victim prevent simultaneous grenade and bola volleys.</summary>
public sealed partial class Wh40kChaosAttackReservationsComponent : Component
{
    public readonly Dictionary<EntityUid, TimeSpan> Attackers = new();

    public EntityUid? Grenadier;
    public EntityUid? Trapper;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan GrenadeUntil;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan BolaUntil;
}
