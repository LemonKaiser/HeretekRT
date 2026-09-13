using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

// Mono - whole file
namespace Content.Shared.Teleportation;

[DataDefinition]
public sealed partial class TeleportSpecifier
{
    [DataField]
    public float TeleportRadius = 100f;

    /// <summary>
    /// Minimum teleport radius to choose, as fraction of maximum radius.
    /// </summary>
    [DataField]
    public float MinRadiusFraction = 0f;

    [DataField]
    public int TeleportAttempts = 20;

    [DataField]
    public SoundSpecifier TeleportSound = new SoundPathSpecifier("/Audio/Effects/teleport_arrival.ogg");

    /// <summary>
    ///     Optional visual entity spawned at both the departure and arrival positions.
    /// </summary>
    [DataField]
    public EntProtoId? TeleportEffect;

    [DataField]
    public bool AvoidSpace = true;

    /// <summary>
    /// Try teleport closer if repeatedly trying to teleport and finding space.
    /// </summary>
    [DataField]
    public bool ForceSafe = true;
}
