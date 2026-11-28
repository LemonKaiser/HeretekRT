using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.FtlEffects;

/// <summary>Replicates the departure ripple's clock without changing shuttle movement.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause, UnsavedComponent]
public sealed partial class Wh40kFtlDepartureComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Started;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Departure;

    [DataField, AutoNetworkedField]
    public bool Entered;

    [DataField, AutoNetworkedField]
    public bool Arriving;

    [DataField, AutoNetworkedField]
    public EntityUid? Lead;
}
