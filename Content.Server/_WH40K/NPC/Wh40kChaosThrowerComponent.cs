using Robust.Shared.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WH40K.NPC;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class Wh40kChaosThrowerComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(6);

    [DataField]
    public float Range = 8f;

    [DataField]
    public float MinRange = 2f;

    [DataField] public TimeSpan Windup = TimeSpan.FromSeconds(0.5);
    [ViewVariables] public EntityUid? WarningTarget;
    [ViewVariables] public MapCoordinates? WarningAim;
    [ViewVariables] public bool WarningExplosive;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan ReadyAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextThrow;
}
