using Content.Shared._WH40K.Npcs;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Npcs;

[RegisterComponent]
public sealed partial class Wh40kAmbientNpcComponent : Component
{
    [DataField(required: true)] public ProtoId<Wh40kNpcProfilePrototype> Profile;
    [ViewVariables] public TimeSpan NextSpeech;
    [ViewVariables] public string? LastPhrase;
}

/// <summary>One saved marker creates one disposable body, without a unique quest identity.</summary>
[RegisterComponent]
public sealed partial class Wh40kAmbientNpcSpawnComponent : Component
{
    [DataField(required: true)] public EntProtoId Prototype;
    [ViewVariables] public EntityUid? Body;
}
