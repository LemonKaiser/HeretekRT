using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Ranged.Events;

/// <summary>
/// Requests a muzzle effect after a shot.
/// </summary>
[Serializable, NetSerializable]
public sealed class MuzzleFlashEvent : EntityEventArgs
{
    public NetEntity Uid;
    public string Prototype;

    public Angle Angle;

    public bool IsBallistic => Prototype is "MuzzleFlashEffect" or "MuzzleFlashEffectSmall";
    public bool IsSmall => Prototype == "MuzzleFlashEffectSmall";

    public MuzzleFlashEvent(NetEntity uid, string prototype, Angle angle)
    {
        Uid = uid;
        Prototype = prototype;
        Angle = angle;
    }
}
