using Content.Shared.Radio;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.ShipPa;

/// <summary>
/// A vessel's situation code, General Quarters klaxon state, speaker status, and SOS distress beacon state.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kShipAlertComponent : Component
{
    public const float MinVolume = 0.2f;

    [DataField, AutoNetworkedField] public ProtoId<Wh40kShipAlertCodePrototype> Code = "Wh40kShipCodeGreen";
    [DataField, AutoNetworkedField] public bool GeneralQuarters;
    [DataField, AutoNetworkedField] public int SpeakersOnline;
    [DataField, AutoNetworkedField] public int SpeakersTotal;
    [DataField, AutoNetworkedField] public bool SpeakersFallback;
    [DataField, AutoNetworkedField] public float Volume = 1f;

    [DataField] public SoundSpecifier GeneralQuartersAlarm = new SoundPathSpecifier("/Audio/Misc/redalert.ogg");
    [DataField] public LocId GeneralQuartersAnnouncement = "wh40k-ship-pa-general-quarters-announcement";
    [DataField] public LocId GeneralQuartersSecureAnnouncement = "wh40k-ship-pa-general-quarters-secure";
    [DataField] public Color GeneralQuartersColor = Color.FromHex("#ff3030");
    [DataField] public SoundSpecifier? GeneralQuartersSound;
    [DataField] public SoundSpecifier? GeneralQuartersSecureSound;

    [DataField] public SoundSpecifier AnnouncementChime = new SoundPathSpecifier("/Audio/Announcements/attention.ogg");
    [DataField] public TimeSpan AnnouncementCooldown = TimeSpan.FromSeconds(5);
    [DataField] public int MaxAnnouncementLength = 256;
    [ViewVariables] public TimeSpan NextAnnouncement;

    [DataField, AutoNetworkedField] public bool Distress;
    [DataField] public TimeSpan DistressDuration = TimeSpan.FromMinutes(20);
    [DataField] public TimeSpan DistressCooldown = TimeSpan.FromSeconds(60);
    [DataField] public ProtoId<RadioChannelPrototype> DistressChannel = "Common";
    [DataField] public Color DistressColor = Color.FromHex("#ff8c1a");
    [ViewVariables] public TimeSpan DistressEnds;
    [ViewVariables] public TimeSpan NextDistress;
}
