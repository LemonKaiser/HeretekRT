using Content.Shared._WH40K.Augments;
using Robust.Shared.Audio;

namespace Content.Server._WH40K.Augments;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class Wh40kAugmentBedComponent : Component
{
    [DataField]
    public TimeSpan RemovalTime = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan FittingTime = TimeSpan.FromSeconds(2);

    [DataField]
    public SoundSpecifier ToolSound = new SoundPathSpecifier("/Audio/_Shitmed/Medical/Surgery/saw.ogg");

    [AutoPausedField]
    public TimeSpan Deadline;

    [AutoPausedField]
    public TimeSpan NextScan;

    internal Wh40kAugmentBedSystem.Operation? Procedure;
    internal EntityUid? ReopenFor;
    internal EntityUid? Patient;
    internal Wh40kAugmentBedState? LastScan;
}
