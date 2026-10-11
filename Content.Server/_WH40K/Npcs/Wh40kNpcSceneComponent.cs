using Robust.Shared.Map;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Npcs;

public enum Wh40kNpcScenePose : byte { Standing, Lying, Seated }

/// <summary>Fixed venue staging, without player sleep/wakeup or dialogue-session state.</summary>
[RegisterComponent]
public sealed partial class Wh40kNpcSceneComponent : Component
{
    [DataField] public Wh40kNpcScenePose Pose;
    [DataField] public List<ProtoId<MarkingPrototype>> Markings = [];
    [DataField] public bool Buckle;
    /// <summary>Light the authored mask-slot cigarette once through native smoking.</summary>
    [DataField] public bool Smoking;
    /// <summary>Applied once after the loadout through the native cuff system.</summary>
    [DataField] public EntProtoId? Cuffs;
    [ViewVariables] public EntityCoordinates? Home;
    [ViewVariables] public Angle Facing;
}
