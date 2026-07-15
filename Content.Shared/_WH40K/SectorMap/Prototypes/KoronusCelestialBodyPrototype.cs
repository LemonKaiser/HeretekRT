using System.Numerics;
using Content.Shared._DV.Planet;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.SectorMap.Prototypes;

/// <summary>
/// A body displayed in the shuttle NAV layer and in the decorative orbital-space background.
/// Orbit values are presentation data, not a physical celestial simulation.
/// </summary>
[Prototype("koronusCelestialBody")]
public sealed partial class KoronusCelestialBodyPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<KoronusSystemPrototype> System;

    [DataField]
    public KoronusCelestialBodyType BodyType = KoronusCelestialBodyType.Planet;

    [DataField(required: true)]
    public string DisplayName = string.Empty;

    [DataField]
    public string Description = string.Empty;

    /// <summary>
    /// Localized short classification shown in the planetary landing interface.
    /// </summary>
    [DataField]
    public LocId Climate = "koronus-planetary-climate-unknown";

    [DataField]
    public float OrbitRadius;

    [DataField]
    public float OrbitPhase;

    /// <summary>
    /// Chooses a new radial placement angle once when the system map is created. This is intended
    /// for static bodies such as asteroid landing targets: keep <see cref="OrbitAngularSpeed"/> at
    /// zero so the selected position remains fixed for the rest of the round.
    /// </summary>
    [DataField]
    public bool RandomizePositionAngle;

    /// <summary>
    /// Visual angular speed in degrees per second. Zero keeps the body stationary.
    /// </summary>
    [DataField]
    public float OrbitAngularSpeed;

    /// <summary>
    /// Optional decorative texture for a non-stellar body. Planet and moon textures are expected
    /// to be 2:1 equirectangular surface maps; the client projects them onto a rotating sphere.
    /// Stars are drawn procedurally and therefore do not need a bitmap asset.
    /// </summary>
    [DataField]
    public ResPath? Texture;

    /// <summary>
    /// Rotation of the surface map around the body's axis, in degrees per second.
    /// </summary>
    [DataField]
    public float SurfaceRotationSpeed = 0.5f;

    /// <summary>
    /// Initial longitudinal offset of the surface map, in degrees.
    /// </summary>
    [DataField]
    public float SurfaceRotationPhase;

    /// <summary>
    /// Visual axial tilt of the projected sphere, in degrees.
    /// </summary>
    [DataField]
    public float AxialTilt;

    /// <summary>
    /// Independent rotation speed of the procedural cloud layer, in degrees per second.
    /// </summary>
    [DataField]
    public float CloudRotationSpeed = 0.75f;

    [DataField]
    public float CloudOpacity = 0.45f;

    [DataField]
    public float CloudScale = 3.4f;

    [DataField]
    public float CloudSeed;

    [DataField]
    public Color CloudColor = Color.White;

    [DataField]
    public Color AtmosphereColor = Color.FromHex("#79C9E8");

    [DataField]
    public float AtmosphereIntensity = 0.85f;

    /// <summary>
    /// Visual radius in system-space metres used by NAV and its interaction target.
    /// </summary>
    [DataField]
    public float NavVisualRadius = 12f;

    /// <summary>
    /// Decorative radius before parallax projection. The client applies the same parallax scale to
    /// this radius and the body's displacement, keeping their geometry consistent. Zero falls back
    /// to <see cref="NavVisualRadius"/>. This remains independently authorable from the NAV radius.
    /// </summary>
    [DataField]
    public float BackgroundVisualRadius;

    /// <summary>
    /// Amount by which the decorative body follows the camera: zero is world-anchored and one is
    /// screen-anchored. Values below one keep the orbital direction while retaining visible motion
    /// against the star field.
    /// </summary>
    [DataField]
    public float BackgroundParallaxSlowness = 0.82f;

    /// <summary>
    /// Additional clearance beyond the NAV visual radius in which a shuttle may begin a
    /// controlled descent.
    /// </summary>
    public const float LandingApproachMargin = 10f;

    /// <summary>
    /// Authoritative landing radius in system-space metres. It follows the visible size of the
    /// body so a landing target cannot remain available hundreds of metres beyond its edge.
    /// </summary>
    public float LandingApproachRadius => MathF.Max(0f, NavVisualRadius) + LandingApproachMargin;

    /// <summary>
    /// Surface available through controlled landing, if this body has one.
    /// </summary>
    [DataField]
    public ProtoId<KoronusPlanetSurfacePrototype>? Surface;
}

public enum KoronusCelestialBodyType : byte
{
    Star,
    Planet,
    Moon,
    Asteroid,
    Station,
}

/// <summary>
/// A preloaded, persistent map surface belonging to a celestial body.
/// </summary>
[Prototype("koronusPlanetSurface")]
public sealed partial class KoronusPlanetSurfacePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ResPath MapPath;

    /// <summary>
    /// Optional stock planet definition. When present, the surface receives the normal planet
    /// atmosphere, gravity, light cycle and biome setup before its authored landing grid is loaded.
    /// </summary>
    [DataField]
    public ProtoId<PlanetPrototype>? Planet;

    [DataField]
    public Vector2 PlayableSize = new(100f, 100f);

    /// <summary>
    /// Decorative biome extension beyond the gameplay perimeter. Generated planet surfaces clamp
    /// this to 25 tiles so terrain fades past the hard edge without becoming an unbounded world.
    /// </summary>
    [DataField]
    public float SceneryBuffer = 25f;

    [DataField]
    public bool PreloadOnRoundStart = true;

    [DataField]
    public bool PauseWhenEmpty = true;

    [DataField]
    public float RepauseDelay = 10f;

    /// <summary>
    /// Existing per-map parallax prototype used by the loaded surface map.
    /// </summary>
    [DataField]
    public string? Parallax;

    /// <summary>
    /// Optional rules applied to the whole surface map. This is deliberately map-wide and has no
    /// orbital safe-zone visualization; terrain remains destructible unless its grid is explicitly
    /// marked as a protected facility.
    /// </summary>
    [DataField]
    public ProtoId<KoronusSafetyProfilePrototype>? SafetyProfile;

    [DataField]
    public float OrbitalLaunchDistance = 500f;

    /// <summary>
    /// Centred vegetation-free area prepared by the planet biome around the landing field.
    /// </summary>
    [DataField]
    public Vector2 LandingClearanceSize;

    /// <summary>
    /// Duration of the atmospheric flight phase of a controlled descent, excluding its startup
    /// and arrival phases. Zero selects an immediate transfer.
    /// </summary>
    [DataField]
    public float LandingTransitTime;

    /// <summary>
    /// Duration of the atmospheric flight phase of a controlled ascent back to orbit.
    /// </summary>
    [DataField]
    public float LaunchTransitTime;

    /// <summary>
    /// BSS-like preparation time before the shuttle enters the atmospheric transit map.
    /// </summary>
    [DataField]
    public float TransitStartupTime = 5f;

    /// <summary>
    /// Final atmospheric arrival phase after flight, before the shuttle is placed on its target map.
    /// </summary>
    [DataField]
    public float TransitArrivalTime = 5f;

}
