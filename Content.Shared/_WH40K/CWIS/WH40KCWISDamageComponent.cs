using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.CWIS;

[RegisterComponent]
public sealed partial class WH40KCWISDamageComponent : Component
{
    [DataField(required: true)]
    public float DamageThreshold;

    public WH40KCWISDamageState State = WH40KCWISDamageState.Intact;
}

[Serializable, NetSerializable]
public enum WH40KCWISVisuals : byte
{
    DamageState
}

[Serializable, NetSerializable]
public enum WH40KCWISDamageState : byte
{
    Intact,
    Damaged,
    HeavilyDamaged,
    Destroyed
}
