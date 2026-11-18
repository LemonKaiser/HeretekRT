using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Blood;

/// <summary>
/// Cosmetic bullet impacts on bare skin or on an equipped garment.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WH40KBulletMarksComponent : Component
{
    public const byte MaxMarks = 3;

    [DataField, AutoNetworkedField]
    public byte ActiveMarks;

    [DataField, AutoNetworkedField]
    public WH40KBulletMarkMaterial Material;

    // Armor wear and the resulting body damage can refer to the same impact.
    public EntityUid? LastProjectile;

    /// <summary>Server-side expiry time for each of the three fixed sprite positions.</summary>
    public TimeSpan[]? ExpiresAt;
}

[Serializable, NetSerializable]
public enum WH40KBulletMarkMaterial : byte
{
    Skin,
    Cloth,
    Armor,
}
