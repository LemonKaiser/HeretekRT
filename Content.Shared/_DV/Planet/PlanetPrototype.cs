using Content.Shared.Atmos;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Markers;
using Robust.Shared.Prototypes;

namespace Content.Shared._DV.Planet;

[Prototype]
public sealed partial class PlanetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = string.Empty;

    /// <summary>
    /// The biome to create the planet with.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<BiomeTemplatePrototype> Biome;

    /// <summary>
    /// Name to give to the map.
    /// </summary>
    [DataField(required: true)]
    public LocId MapName;

    /// <summary>
    /// Ambient lighting for the map.
    /// </summary>
    [DataField]
    public Color MapLight = Color.FromHex("#D8B059");

    /// <summary>
    /// Whether the map light and sun shadows advance through a day/night cycle.
    /// Static surfaces such as asteroids can disable this while retaining authored ambient light.
    /// </summary>
    [DataField]
    public bool LightCycleEnabled = true;

    /// <summary>
    /// Optional length of the illuminated part of the planet's light cycle.
    /// When paired with <see cref="NightDuration"/>, the map holds a distinct night phase.
    /// </summary>
    [DataField]
    public TimeSpan? DayDuration;

    /// <summary>
    /// Optional length of the dark part of the planet's light cycle.
    /// </summary>
    [DataField]
    public TimeSpan? NightDuration;

    /// <summary>
    /// Components to add to the map.
    /// </summary>
    [DataField]
    public ComponentRegistry? AddedComponents;

    /// <summary>
    /// The gas mixture to use for the atmosphere.
    /// </summary>
    [DataField(required: true)]
    public GasMixture Atmosphere = new();

    /// <summary>
    /// Biome layers to add to the map, i.e. ores.
    /// </summary>
    [DataField]
    public List<ProtoId<BiomeMarkerLayerPrototype>> BiomeMarkerLayers = new();
}
