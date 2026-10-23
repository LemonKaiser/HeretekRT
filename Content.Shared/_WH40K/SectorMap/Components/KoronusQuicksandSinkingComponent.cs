using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.SectorMap.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause(Dirty = true)]
public sealed partial class KoronusQuicksandSinkingComponent : Component
{
    [DataField, AutoNetworkedField]
    public float SpeedModifier = 1f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EnteredAt;
}
