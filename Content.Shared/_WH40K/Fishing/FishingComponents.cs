using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Fishing;

[RegisterComponent, NetworkedComponent]
public sealed partial class FishingRodComponent : Component
{
    [DataField]
    public TimeSpan CatchTime = TimeSpan.FromSeconds(4);
}

/// <summary>
/// A map-authored point where a rod can be used.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FishingSpotComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Catch = "FoodMeatFish";
}

[Serializable, NetSerializable]
public sealed partial class FishingDoAfterEvent : SimpleDoAfterEvent;
