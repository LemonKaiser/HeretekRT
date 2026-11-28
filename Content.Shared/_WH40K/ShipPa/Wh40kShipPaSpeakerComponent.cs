using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.ShipPa;

/// <summary>
/// A physical Vox-PA speaker (or air alarm fallback) anchored to a ship grid.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kShipPaSpeakerComponent : Component
{
    [DataField, AutoNetworkedField] public bool Enabled;
    [DataField] public float Range = 14f;
    [DataField] public float Volume = 0f;
    [DataField] public FixedPoint2 DistortionDamage = 60;
    [DataField] public SoundSpecifier? StaticSound = new SoundPathSpecifier("/Audio/_WH40K/ShipPa/speaker_static.ogg");
    [DataField] public bool Fallback;
    [DataField, AutoNetworkedField] public float Distortion;
    [DataField, AutoNetworkedField] public bool Broken;
    [ViewVariables] public TimeSpan? BroadcastingUntil;
}
