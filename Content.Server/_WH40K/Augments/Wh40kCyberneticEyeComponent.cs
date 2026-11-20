using Content.Shared.Humanoid.Markings;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Augments;

[RegisterComponent]
public sealed partial class Wh40kCyberneticEyeComponent : Component
{
    [DataField(required: true)]
    public ProtoId<MarkingPrototype> Marking;
}
