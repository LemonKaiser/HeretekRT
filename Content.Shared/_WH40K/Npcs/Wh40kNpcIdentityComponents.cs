using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Npcs;

/// <summary>Placed spawn/home identity; this component does not automatically spawn a body.</summary>
[RegisterComponent]
public sealed partial class Wh40kNpcAnchorComponent : Component
{
    [DataField(required: true)] public ProtoId<Wh40kNpcAnchorPrototype> Anchor;
}

/// <summary>Replaceable runtime body. Never persist this entity uid as quest/NPC identity.</summary>
[RegisterComponent]
public sealed partial class Wh40kNpcIdentityComponent : Component
{
    [DataField(required: true)] public ProtoId<Wh40kNpcAnchorPrototype> Anchor;
}
