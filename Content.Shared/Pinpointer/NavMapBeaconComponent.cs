using Robust.Shared.GameStates;

namespace Content.Shared.Pinpointer;

/// <summary>
/// Will show a marker on a NavMap.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedNavMapSystem))]
[AutoGenerateComponentState]
public sealed partial class NavMapBeaconComponent : Component
{
    /// <summary>
    /// Defaults to entity name if nothing found.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    [AutoNetworkedField]
    public string? Text;

    /// <summary>
    /// Optional English label. Text remains the Russian label and fallback for older maps.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    [AutoNetworkedField]
    public string? TextEnglish;

    /// <summary>
    /// A localization string displayed in the viewer's language when Text is empty.
    /// Used so that mappers can still override Text while mapping.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public LocId? DefaultText;

    [ViewVariables(VVAccess.ReadWrite), DataField]
    [AutoNetworkedField]
    public Color Color = Color.Orange;

    /// <summary>
    /// Only enabled beacons can be seen on a station map.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    [AutoNetworkedField]
    public bool Enabled = true;
}
