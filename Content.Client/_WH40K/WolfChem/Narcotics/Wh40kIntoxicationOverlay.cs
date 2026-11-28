using System.Numerics;
using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.WolfChem.Narcotics;

/// <summary>
/// Draws the local player's strongest high and any crash with one shader. Static and trail-free under reduced motion.
/// A neuroware host sees its programs through a digital shader of its own.
/// </summary>
public sealed class Wh40kIntoxicationOverlay : Overlay
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private const float Follow = 1.5f;
    private const float PhaseFollow = 2.5f;
    private const float KickFade = 1.2f;

    private static readonly Dictionary<Wh40kDrugFamily, (float Heavy, float Overdose)> Trails = new()
    {
        [Wh40kDrugFamily.Psychedelic] = (0.85f, 0.93f),
        [Wh40kDrugFamily.Opioid] = (0.7f, 0.88f),
        [Wh40kDrugFamily.Cannabinoid] = (0.6f, 0.85f),
        [Wh40kDrugFamily.Empathogen] = (0.55f, 0.8f),
        [Wh40kDrugFamily.Stimulant] = (0f, 0.65f),
    };

    private const float TrailSpread = 0.0015f;
    private const float PsychedelicSpread = 0.004f;

    private readonly ShaderInstance _shader;
    private readonly ShaderInstance _neurowareShader;
    private readonly ShaderInstance _trailShader;
    private readonly Wh40kIntoxicationSystem _system;

    private Wh40kDrugFamily _family;
    private bool _synthetic;
    private Wh40kDrugPhase _phase;
    private float _intensity;
    private float _heavy;
    private float _overdose;
    private float _kick;
    private float _crash;
    private float _pulseRate = 1.2f;
    private float _trail;
    private float _frameSeconds;

    private IRenderTexture? _trailA;
    private IRenderTexture? _trailB;
    private bool _trailFlip;
    private bool _trailValid;

    public Wh40kIntoxicationOverlay(Wh40kIntoxicationSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        _shader = _proto.Index<ShaderPrototype>("Wh40kIntoxication").InstanceUnique();
        _neurowareShader = _proto.Index<ShaderPrototype>("Wh40kNeuroware").InstanceUnique();
        _trailShader = _proto.Index<ShaderPrototype>("Wh40kIntoxicationTrail").InstanceUnique();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var targetIntensity = 0f;
        var targetCrash = 0f;
        var phase = Wh40kDrugPhase.None;

        if (_player.LocalEntity is { } local
            && _entities.TryGetComponent(local, out Wh40kIntoxicationComponent? comp))
        {
            _synthetic = _system.IsNeurowareHost(local);
            if (_system.DominantHigh(comp) is { } high)
            {
                phase = high.Phase;
                targetIntensity = high.Strength;
                _family = high.Family;
                _pulseRate = 1.2f + 0.6f * high.Tier;
            }

            foreach (var profile in _system.ActiveCrashes(comp))
            {
                if (_proto.TryIndex(profile, out var proto) && proto.Crash != null)
                    targetCrash = Math.Max(targetCrash, proto.Crash.Overlay);
            }
        }

        if (phase > _phase && phase != Wh40kDrugPhase.None)
            _kick = 1f;

        _phase = phase;
        _frameSeconds = args.DeltaSeconds;

        var step = Math.Min(1f, args.DeltaSeconds * Follow);
        _intensity += (targetIntensity - _intensity) * step;
        _crash += (targetCrash - _crash) * step;

        var phaseStep = Math.Min(1f, args.DeltaSeconds * PhaseFollow);
        _heavy += ((phase >= Wh40kDrugPhase.Heavy ? 1f : 0f) - _heavy) * phaseStep;
        _overdose += ((phase == Wh40kDrugPhase.Overdose ? 1f : 0f) - _overdose) * phaseStep;
        _kick = Math.Max(0f, _kick - args.DeltaSeconds * KickFade);

        var targetTrail = 0f;
        if (Trails.TryGetValue(_family, out var trail))
            targetTrail = (trail.Heavy * _heavy + (trail.Overdose - trail.Heavy) * _overdose) * (0.5f + 0.5f * _intensity);
        _trail += (targetTrail - _trail) * phaseStep;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_intensity < 0.01f && _crash < 0.01f && _kick < 0.01f)
        {
            _trailValid = false;
            return false;
        }

        return _entities.TryGetComponent(_player.LocalEntity, out EyeComponent? eye) && args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        var motion = !_config.GetCVar(CCVars.ReducedMotion);
        Texture screen = ScreenTexture;
        if (motion && _trail > 0.01f)
            screen = Trail(handle, ScreenTexture, MathF.Pow(_trail, _frameSeconds * 60f));
        else
            _trailValid = false;

        var shader = _synthetic ? _neurowareShader : _shader;
        shader.SetParameter("SCREEN_TEXTURE", screen);
        shader.SetParameter("family", (float) _family);
        shader.SetParameter("intensity", _intensity);
        shader.SetParameter("heavy", _heavy);
        shader.SetParameter("overdose", _overdose);
        shader.SetParameter("kick", _kick);
        shader.SetParameter("crash", _crash);
        shader.SetParameter("motion", motion ? 1f : 0f);
        shader.SetParameter("pulseRate", _pulseRate);
        handle.UseShader(shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }

    private Texture Trail(DrawingHandleWorld handle, Texture screen, float persistence)
    {
        var size = screen.Size;
        if (_trailA == null || _trailB == null || _trailA.Size != size)
        {
            _trailA?.Dispose();
            _trailB?.Dispose();
            var format = new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb);
            _trailA = _clyde.CreateRenderTarget(size, format, name: "wh40k-intoxication-trail-a");
            _trailB = _clyde.CreateRenderTarget(size, format, name: "wh40k-intoxication-trail-b");
            _trailValid = false;
        }

        var (from, to) = _trailFlip ? (_trailB, _trailA) : (_trailA, _trailB);
        _trailFlip = !_trailFlip;

        _trailShader.SetParameter("SCREEN_TEXTURE", screen);
        _trailShader.SetParameter("HISTORY", from.Texture);
        _trailShader.SetParameter("persistence", _trailValid ? persistence : 0f);
        _trailShader.SetParameter("spread", _family == Wh40kDrugFamily.Psychedelic ? PsychedelicSpread : TrailSpread);
        handle.RenderInRenderTarget(to, () =>
        {
            handle.UseShader(_trailShader);
            handle.SetTransform(Matrix3x2.Identity);
            handle.DrawRect(new Box2(0, 0, size.X, size.Y), Color.White);
            handle.UseShader(null);
        }, null);

        _trailValid = true;
        return to.Texture;
    }

    protected override void DisposeBehavior()
    {
        _trailA?.Dispose();
        _trailB?.Dispose();
        base.DisposeBehavior();
    }
}
