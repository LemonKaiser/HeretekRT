namespace Content.Shared._WH40K.ShipPa;

/// <summary>
/// Marker for a client-local Vox-PA audio voice entity.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kShipPaAudioComponent : Component
{
    [DataField] public int BroadcastId;
    [DataField] public EntityUid? Speaker;
}
