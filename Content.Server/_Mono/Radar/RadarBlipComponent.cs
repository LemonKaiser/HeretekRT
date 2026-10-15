using Content.Shared._Mono.Radar;

namespace Content.Server._Mono.Radar;

/// <summary>
/// These handle objects which should be represented by radar blips.
/// </summary>
[RegisterComponent]
public sealed partial class RadarBlipComponent : Component
{
    /// <summary>
    /// Whether this blip should be shown even when parented to a grid.
    /// </summary>
    [DataField]
    public bool RequireNoGrid = false;

    /// <summary>
    /// Whether this blip should be visible on radar across different grids.
    /// </summary>
    [DataField]
    public bool VisibleFromOtherGrids = true;

    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Do not show the blip beyond this distance to the viewing mass scanner.
    /// </summary>
    [DataField]
    public float MaxDistance = 2048f;

    /// <summary>
    /// Use the viewing radar's range instead of a fixed contact distance.
    /// </summary>
    [DataField]
    public bool UseRadarRange;

    /// <summary>
    /// If not null, show up as this config if on a grid.
    /// </summary>
    [DataField]
    public BlipConfig? GridConfig = null;

    #region Backwards Compatibility

    [DataField("radarColor")]
    public Color _radarColor { set => Config.Color = value; get => Config.Color; }

    // note that the original code arbitrarily *3'd the size
    [DataField("scale")]
    public float _scale {
        set => Config.Bounds = new Box2(-value * 1.5f, -value * 1.5f, value * 1.5f, value * 1.5f);
        get => (Config.Bounds.Width + Config.Bounds.Height) / 6f;
    }

    [DataField("shape")]
    public RadarBlipShape _shape { set => Config.Shape = value; get => Config.Shape; }

    #endregion

    /// <summary>
    /// How to display normally. Keep after the legacy aliases for generated copying, and load it last from YAML.
    /// Legacy scale cannot represent rectangular bounds.
    /// </summary>
    [DataField(priority: 0)]
    public BlipConfig Config = new();
}
