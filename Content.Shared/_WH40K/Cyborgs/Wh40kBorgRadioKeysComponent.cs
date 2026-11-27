using Content.Shared.Radio;
using Content.Shared.Silicons.Borgs;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Cyborgs;

/// <summary>
/// Tracks a cyborg's built-in radio channels so encryption keys add onto them instead of wiping them.
/// Key slots open only while the chassis lock is unlocked.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kBorgRadioKeysComponent : Component
{
    [ViewVariables]
    public HashSet<ProtoId<RadioChannelPrototype>> BaseTransmit = new();

    [ViewVariables]
    public HashSet<ProtoId<RadioChannelPrototype>> BaseReceive = new();
}

/// <summary>
/// Raised locally on a cyborg chassis after its module type has been selected.
/// </summary>
[ByRefEvent]
public readonly record struct Wh40kBorgTypeSelectedEvent(ProtoId<BorgTypePrototype> BorgType);
