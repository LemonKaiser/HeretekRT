using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Holographic decoy projector: spawns wandering visual duplicates around the current room and swaps the wearer to a safe tile.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kHoloProjectorComponent : Component
{
    [DataField]
    public EntProtoId Action = "WH40KActionHoloProject";

    [ViewVariables]
    public EntityUid? ActionEntity;

    [DataField]
    public EntProtoId Decoy = "WH40KHoloDecoy";

    [DataField]
    public EntProtoId Flicker = "WH40KHoloDecoyPuff";

    [DataField]
    public SoundSpecifier CastSound = new SoundPathSpecifier("/Audio/Effects/stealthoff.ogg");

    [DataField]
    public int Copies = 2;

    [DataField]
    public int Range = 5;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kHoloDecoyComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Source;

    [DataField]
    public EntProtoId Puff = "WH40KHoloDecoyPuff";

    [ViewVariables]
    public bool Copied;
}

public sealed partial class Wh40kHoloProjectEvent : InstantActionEvent;
