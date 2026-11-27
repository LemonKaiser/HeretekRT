using System.Numerics;
using Content.Shared._WH40K.Explosion;
using Content.Shared.Explosion.Components;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Map;

namespace Content.Client._WH40K.Explosion;

/// <summary>
/// Tracks the distortion ring each explosion throws out and feeds it to <see cref="Wh40kExplosionShockwaveOverlay"/>.
/// </summary>
public sealed partial class Wh40kExplosionShockwaveSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedMapSystem _mapMan = default!;
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    private const float MinExplosionIntensity = 100f;

    /// <summary>Tiles per second the front leaves the epicentre at. It decelerates from there.</summary>
    private const float FrontSpeed = 45f;

    private const float MinDuration = 0.2f;
    private const float MaxDuration = 1.2f;

    /// <summary>Half thickness of the band, as a fraction of the wave's reach, clamped to the two below.</summary>
    private const float BandFraction = 0.25f;
    private const float MinBand = 1.2f;
    private const float MaxBand = 4f;

    /// <summary>Peak displacement as a fraction of the wave's reach, clamped to the two below, in tiles.</summary>
    private const float StrengthFraction = 0.06f;
    private const float MinStrength = 0.12f;
    private const float MaxStrength = 0.75f;

    /// <summary>Waves kept alive at once. The overlay only draws the first few of these it finds on screen.</summary>
    private const int MaxTracked = 8;

    private readonly List<Shockwave> _waves = new();

    /// <summary>Live waves, updated once per frame.</summary>
    public IReadOnlyList<Shockwave> Shockwaves => _waves;

    public override void Initialize()
    {
        base.Initialize();

        // ComponentInit on ExplosionVisualsComponent belongs to ExplosionOverlaySystem; hook startup instead.
        SubscribeLocalEvent<ExplosionVisualsComponent, ComponentStartup>(OnVisualsStartup);

        _overlayMan.AddOverlay(new Wh40kExplosionShockwaveOverlay(this));
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_overlayMan.TryGetOverlay<Wh40kExplosionShockwaveOverlay>(out var overlay))
        {
            _overlayMan.RemoveOverlay(overlay);
            overlay.Dispose();
        }
        _waves.Clear();
    }

    private void OnVisualsStartup(Entity<ExplosionVisualsComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.TotalIntensity < MinExplosionIntensity || !_cfg.GetCVar(Wh40kShockwaveCVars.Enabled))
            return;

        var epicenter = ent.Comp.Epicenter;

        if (epicenter.MapId == MapId.Nullspace || !_mapMan.MapExists(epicenter.MapId))
            return;

        // One intensity entry per expansion step, and a step is one tile of blast radius.
        var reach = ent.Comp.Intensity.Count + _cfg.GetCVar(Wh40kShockwaveCVars.Overshoot);

        if (reach <= 0f)
            return;

        var wave = new Shockwave
        {
            Position = epicenter,
            MaxRadius = reach,
            // The eased radius below leaves at twice the average speed, so this matches FrontSpeed at the epicentre.
            Duration = Math.Clamp(2f * reach / FrontSpeed, MinDuration, MaxDuration),
            BandWidth = Math.Clamp(reach * BandFraction, MinBand, MaxBand),
            PeakStrength = Math.Clamp(reach * StrengthFraction, MinStrength, MaxStrength)
                           * Math.Max(_cfg.GetCVar(Wh40kShockwaveCVars.Strength), 0f),
        };

        // Ride the grid the blast went off on, so a wave on a moving shuttle doesn't slide off its epicentre.
        if (_mapMan.TryFindGridAt(epicenter, out var gridUid, out _))
        {
            wave.Grid = gridUid;
            wave.GridPosition = Vector2.Transform(epicenter.Position, _xform.GetInvWorldMatrix(gridUid));
        }

        if (_waves.Count >= MaxTracked)
            _waves.RemoveAt(0);

        _waves.Add(wave);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        for (var i = _waves.Count - 1; i >= 0; i--)
        {
            var wave = _waves[i];
            wave.Elapsed += frameTime;

            if (wave.Elapsed >= wave.Duration)
            {
                _waves.RemoveAt(i);
                continue;
            }

            if (wave.Grid is { } grid && !Deleted(grid))
                wave.Position = _xform.ToMapCoordinates(new EntityCoordinates(grid, wave.GridPosition));

            var t = wave.Elapsed / wave.Duration;

            wave.Radius = wave.MaxRadius * t * (2f - t); // Fast off the epicentre, slowing into the edge.
            wave.Width = wave.BandWidth * (0.5f + 0.5f * t); // Spreads out as it loses its edge.
            wave.Strength = wave.PeakStrength * (1f - t * t); // Holds, then dies at the edge.
        }
    }

    /// <summary>One expanding ring of distortion. Everything is in tiles unless stated otherwise.</summary>
    public sealed class Shockwave
    {
        /// <summary>Epicentre in world terms, refreshed each frame while its grid lives.</summary>
        public MapCoordinates Position;

        /// <summary>Grid the epicentre sits on, if any, and where on it.</summary>
        public EntityUid? Grid;
        public Vector2 GridPosition;

        public float MaxRadius;
        public float BandWidth;
        public float PeakStrength;

        /// <summary>Seconds the wave takes to reach <see cref="MaxRadius"/>.</summary>
        public float Duration;
        public float Elapsed;

        /// <summary>This frame's front radius, half thickness and peak displacement.</summary>
        public float Radius;
        public float Width;
        public float Strength;
    }
}
