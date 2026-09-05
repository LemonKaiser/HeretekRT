using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Shared._WH40K.Augments;

[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentActivatableUIComponent : Component
{
    [DataField(required: true, customTypeSerializer: typeof(EnumSerializer))]
    public Enum? Key;
}
