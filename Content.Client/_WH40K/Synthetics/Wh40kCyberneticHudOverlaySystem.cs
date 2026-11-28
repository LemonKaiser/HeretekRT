using System.Linq;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WH40K.Synthetics;
using Content.Shared.CCVar;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Client._WH40K.Synthetics;

[UsedImplicitly]
public sealed partial class Wh40kCyberneticHudOverlaySystem : EntitySystem
{
    private const string FaultSound = "/Audio/Machines/beep.ogg";
    private const string AlarmSound = "/Audio/Machines/beep.ogg";
    private const string StandbySound = "/Audio/Machines/buzz-sigh.ogg";

    private const float SlideSeconds = 0.25f;
    private const float PanicSeconds = 2f;
    private const float BannerFadeIn = 1.2f;
    private const float BannerHold = 5f;
    private const float BannerFadeOut = 2f;
    private const int IdleLines = 4;

    private static readonly string[] SpinnerFrames = { "|", "/", "-", "\\" };

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private Wh40kCyberneticHudOverlay _hud = default!;
    private Wh40kCyberneticScreenOverlay _screen = default!;
    private bool _hudAdded;
    private bool _screenAdded;
    private bool _enabled = true;
    private float _scale = 1f;
    private bool _reducedMotion;

    private readonly Dictionary<(TargetBodyPart, string), float> _slides = new();
    private float _time;
    private float _spinner;
    private float _nextBlip;
    private float _nextAlarm;
    private float _nextGlitch;
    private float _jitterUntil;
    private float _deadTime;
    private bool _wasStandby;

    public override void Initialize()
    {
        base.Initialize();
        _hud = new Wh40kCyberneticHudOverlay();
        _screen = new Wh40kCyberneticScreenOverlay();

        Subs.CVar(_cfg, Wh40kSyntheticsCVars.CyberneticHud, value => _enabled = value, true);
        Subs.CVar(_cfg, Wh40kSyntheticsCVars.CyberneticHudScale, value => _scale = Math.Clamp(value, 0.6f, 2.5f), true);
        Subs.CVar(_cfg, CCVars.ReducedMotion, value => _reducedMotion = value, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        SetOverlays(false);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _time += frameTime;

        if (_player.LocalEntity is not { } local || !_enabled ||
            !TryComp<Wh40kCyberneticHudComponent>(local, out var hud))
        {
            Fade(frameTime, out var idle);
            if (idle)
            {
                SetOverlays(false);
                _slides.Clear();
                _deadTime = 0f;
                _wasStandby = false;
            }

            return;
        }

        var dead = hud.Dead;
        var standby = !dead && (hud.Critical || hud.Rebooting);
        var tier = Wh40kCyberneticHudHelper.ComputeTier(hud);
        var strain = Wh40kCyberneticHudHelper.ComputeStrain(hud);
        var neuroware = Wh40kNeurowareIntensity(local);
        if (neuroware > 0f)
        {
            strain = MathF.Max(strain, neuroware);
            if (neuroware >= Wh40kNeurowareGlitchAt && tier < Wh40kCyberneticHudTier.Heavy)
                tier = Wh40kCyberneticHudTier.Heavy;
            else if (tier < Wh40kCyberneticHudTier.Moderate)
                tier = Wh40kCyberneticHudTier.Moderate;
        }

        _hud.Scale = _scale;
        BuildRows(hud, frameTime);
        BuildSystem(hud, tier);
        UpdateBanner(hud, tier, dead, standby);
        UpdateDeath(dead, frameTime);
        UpdateMotion(tier, strain, standby, frameTime);

        var (showSystem, showDiagnostics) = Wh40kCyberneticHudLayout.Visible(tier, standby, dead, _hud.Rows.Count);
        Approach(ref _hud.SystemAlpha, showSystem ? 1f : 0f, frameTime);
        Approach(ref _hud.DiagnosticsAlpha, showDiagnostics ? 1f : 0f, frameTime);
        if (dead)
        {
            _hud.StandbyAlpha = 0f;
            _screen.Standby = 0f;
        }
        else
        {
            Approach(ref _hud.StandbyAlpha, standby ? 1f : 0f, frameTime);
            Approach(ref _screen.Standby, standby ? 0.85f : 0f, frameTime);
        }

        if (standby != _wasStandby)
        {
            _wasStandby = standby;
            if (standby)
                _audio.PlayGlobal(new ResolvedPathSpecifier(StandbySound), Filter.Local(), false, AudioParams.Default.WithVolume(-12f));
        }

        UpdateAlarm(strain, dead || standby);
        SetOverlays(true);
    }

    private void BuildRows(Wh40kCyberneticHudComponent hud, float frameTime)
    {
        _hud.Rows.Clear();
        var seen = new HashSet<(TargetBodyPart, string)>();
        var fresh = false;

        foreach (var fault in hud.Faults)
        {
            var key = (fault.Part, fault.Line);
            seen.Add(key);
            if (!_slides.TryGetValue(key, out var slide))
            {
                slide = _reducedMotion ? 1f : 0f;
                fresh = true;
            }

            slide = Math.Min(1f, slide + frameTime / SlideSeconds);
            _slides[key] = slide;

            _hud.Rows.Add(new Wh40kCyberneticHudOverlay.Row(
                Loc.GetString(TagKey(fault.Severity)),
                Loc.GetString(Wh40kCyberneticHudHelper.PartLocKey(fault.Part)),
                Loc.GetString(fault.Line),
                fault.Severity,
                slide));
        }

        foreach (var key in _slides.Keys.Where(k => !seen.Contains(k)).ToArray())
            _slides.Remove(key);

        if (fresh && _time >= _nextBlip)
        {
            _nextBlip = _time + 0.25f;
            _audio.PlayGlobal(new ResolvedPathSpecifier(FaultSound), Filter.Local(), false, AudioParams.Default.WithVolume(-14f));
        }
    }

    private void UpdateAlarm(float strain, bool silent)
    {
        if (silent || strain < Wh40kCyberneticHudHelper.HeavyAt)
        {
            _nextAlarm = 0f;
            return;
        }

        if (_time < _nextAlarm)
            return;

        var urgency = Math.Clamp((strain - Wh40kCyberneticHudHelper.HeavyAt) / (1f - Wh40kCyberneticHudHelper.HeavyAt), 0f, 1f);
        _nextAlarm = _time + MathHelper.Lerp(2.5f, 0.8f, urgency);
        _audio.PlayGlobal(new ResolvedPathSpecifier(AlarmSound), Filter.Local(), false, AudioParams.Default.WithVolume(-16f));
    }

    private void BuildSystem(Wh40kCyberneticHudComponent hud, Wh40kCyberneticHudTier tier)
    {
        var rate = 3.5f * Math.Clamp(hud.Integrity, 0.15f, 1f);
        _spinner += rate * 0.016f;
        if (tier >= Wh40kCyberneticHudTier.Moderate && !_reducedMotion && _random.Prob(0.04f))
            _spinner += 1f;

        _hud.Spinner = SpinnerFrames[(int) _spinner % SpinnerFrames.Length];

        _hud.SystemRows.Clear();
        _hud.SystemRows.Add(GaugeRow("wh40k-chassis-ui-row-integrity", hud.Integrity));
        if (hud.Power >= 0f)
            _hud.SystemRows.Add(GaugeRow("wh40k-chassis-ui-row-power", hud.Power));
        _hud.SystemRows.Add(GaugeRow("wh40k-chassis-ui-row-servos", hud.Servos));
        _hud.SystemRows.Add(GaugeRow("wh40k-chassis-ui-row-sensors", hud.Sensors));
        if (hud.ChassisHeat > 0.01f || hud.Overclocked)
            _hud.SystemRows.Add(GaugeRow("wh40k-chassis-ui-row-heat", hud.ChassisHeat));
        if (hud.RadCollector >= 0f)
            _hud.SystemRows.Add(GaugeRow("wh40k-hud-row-rad", hud.RadCollector));
        if (hud.CoreTemperature >= 0f)
        {
            _hud.SystemRows.Add(new Wh40kCyberneticHudOverlay.SystemRow(
                Loc.GetString("wh40k-chassis-ui-row-core"),
                null,
                Loc.GetString("wh40k-chassis-ui-row-core-temp", ("value", (int) MathF.Round(hud.CoreTemperature)))));
        }

        _hud.FaultsLabel = Loc.GetString("wh40k-chassis-ui-row-faults");
        _hud.FaultCount = hud.Faults.Count;

        if (hud.Advice.Length > 0)
        {
            var advice = Loc.GetString(hud.Advice);
            _hud.Status = Loc.GetString("wh40k-hud-advice", ("advice", advice));
            _hud.StatusPrefix = Math.Max(0, _hud.Status.Length - advice.Length);
        }
        else
        {
            _hud.Status = Loc.GetString($"wh40k-hud-idle-{(int) (_time / 6f) % IdleLines + 1}");
            _hud.StatusPrefix = 0;
        }
    }

    private Wh40kCyberneticHudOverlay.SystemRow GaugeRow(string labelKey, float fraction) =>
        new(Loc.GetString(labelKey), Wh40kTerminalStyle.Gauge(fraction, 6), Percent(fraction));

    private void UpdateBanner(Wh40kCyberneticHudComponent hud, Wh40kCyberneticHudTier tier, bool dead, bool standby)
    {
        _hud.StandbyTitle = Loc.GetString(hud.Rebooting ? "wh40k-hud-banner-rebooting" : "wh40k-hud-banner-standby");
        _hud.RebootText = Loc.GetString("wh40k-hud-banner-reboot-hint");
        _hud.RebootVisible = (int) (_time * 1.6f) % 2 == 0;

        if (dead || standby)
        {
            _hud.BannerAlpha = Math.Max(0f, _hud.BannerAlpha - 0.08f);
            return;
        }

        if (hud.Banner.Length > 0)
        {
            _hud.Banner = Loc.GetString(hud.Banner);
            _hud.BannerSeverity = hud.LowPower ? Wh40kCyberneticSeverity.Warn : Wh40kCyberneticSeverity.Crit;
        }
        else if (tier == Wh40kCyberneticHudTier.Heavy)
        {
            _hud.Banner = Loc.GetString("wh40k-hud-banner-integrity", ("value", Percent(hud.Integrity)));
            _hud.BannerSeverity = Wh40kCyberneticSeverity.Crit;
        }
        else
        {
            _hud.BannerAlpha = Math.Max(0f, _hud.BannerAlpha - 0.08f);
            return;
        }

        _hud.BannerAlpha = Math.Min(1f, _hud.BannerAlpha + 0.08f);
    }

    private void UpdateDeath(bool dead, float frameTime)
    {
        if (!dead)
        {
            _deadTime = 0f;
            _hud.PanicAlpha = Math.Max(0f, _hud.PanicAlpha - frameTime * 3f);
            _hud.DeathAlpha = Math.Max(0f, _hud.DeathAlpha - frameTime * 2f);
            return;
        }

        if (_deadTime == 0f)
        {
            _hud.PanicTitle = Loc.GetString("wh40k-hud-banner-panic");
            _hud.Panic.Clear();
            for (var i = 0; i < 6; i++)
            {
                _hud.Panic.Add(Loc.GetString(
                    "wh40k-hud-panic-dump",
                    ("address", _random.Next(0x10000000, 0x7fffffff).ToString("x8"))));
            }
        }

        _deadTime += frameTime;
        _hud.DeathTitle = Loc.GetString("wh40k-hud-death-banner");
        _hud.DeathSub = Loc.GetString("wh40k-hud-death-sub");

        _hud.PanicAlpha = _deadTime < PanicSeconds
            ? Math.Min(1f, _deadTime * 6f)
            : Math.Max(0f, 1f - (_deadTime - PanicSeconds) * 2f);

        var since = _deadTime - PanicSeconds;
        _hud.DeathAlpha = since <= 0f || since >= BannerFadeIn + BannerHold + BannerFadeOut
            ? 0f
            : since < BannerFadeIn
                ? since / BannerFadeIn
                : 1f - Math.Clamp((since - BannerFadeIn - BannerHold) / BannerFadeOut, 0f, 1f);
    }

    private void UpdateMotion(Wh40kCyberneticHudTier tier, float strain, bool standby, float frameTime)
    {
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 2.4f);
        var tint = tier switch
        {
            Wh40kCyberneticHudTier.Heavy => 0.45f + 0.30f * (_reducedMotion ? 0.5f : pulse),
            Wh40kCyberneticHudTier.Moderate => 0.20f + 0.18f * strain,
            _ => 0f,
        };

        Approach(ref _screen.Tint, standby ? 0f : tint, frameTime);

        if (_reducedMotion || tier < Wh40kCyberneticHudTier.Heavy || standby)
        {
            _screen.Glitch = 0f;
            _hud.Jitter = 0f;
            return;
        }

        if (_time >= _nextGlitch)
        {
            _nextGlitch = _time + _random.NextFloat(1.1f, 3.2f) * (1.4f - strain);
            _screen.Glitch = _random.NextFloat(0.35f, 1f);
            _screen.Slice = _random.NextFloat(0.1f, 0.9f);
            _jitterUntil = _time + 0.18f;
        }
        else
        {
            _screen.Glitch = 0f;
        }

        _hud.Jitter = _time < _jitterUntil ? (_random.Prob(0.5f) ? 1f : -1f) : 0f;
    }

    private void Fade(float frameTime, out bool idle)
    {
        Approach(ref _hud.SystemAlpha, 0f, frameTime);
        Approach(ref _hud.DiagnosticsAlpha, 0f, frameTime);
        Approach(ref _hud.StandbyAlpha, 0f, frameTime);
        Approach(ref _screen.Tint, 0f, frameTime);
        Approach(ref _screen.Standby, 0f, frameTime);
        _hud.BannerAlpha = Math.Max(0f, _hud.BannerAlpha - frameTime * 3f);
        _hud.PanicAlpha = Math.Max(0f, _hud.PanicAlpha - frameTime * 3f);
        _hud.DeathAlpha = Math.Max(0f, _hud.DeathAlpha - frameTime * 2f);
        _screen.Glitch = 0f;
        _hud.Jitter = 0f;

        idle = _hud.SystemAlpha <= 0f && _hud.DiagnosticsAlpha <= 0f &&
               _hud.StandbyAlpha <= 0f && _hud.BannerAlpha <= 0f && _hud.PanicAlpha <= 0f &&
               _hud.DeathAlpha <= 0f && _screen.Tint <= 0f && _screen.Standby <= 0f;
    }

    private static void Approach(ref float value, float target, float frameTime)
    {
        value += (target - value) * Math.Min(1f, frameTime * 4f);
        if (MathF.Abs(target - value) < 0.004f)
            value = target;
    }

    private static string Percent(float fraction) => $"{(int) MathF.Round(Math.Clamp(fraction, 0f, 1f) * 100f)}%";

    private static string TagKey(Wh40kCyberneticSeverity severity) => severity switch
    {
        Wh40kCyberneticSeverity.Warn => "wh40k-hud-tag-warn",
        Wh40kCyberneticSeverity.Crit => "wh40k-hud-tag-crit",
        Wh40kCyberneticSeverity.Fail => "wh40k-hud-tag-fail",
        _ => "wh40k-hud-tag-info",
    };

    private void SetOverlays(bool on)
    {
        if (on != _hudAdded)
        {
            _hudAdded = on;
            if (on)
                _overlays.AddOverlay(_hud);
            else
                _overlays.RemoveOverlay(_hud);
        }

        if (on == _screenAdded)
            return;

        _screenAdded = on;
        if (on)
            _overlays.AddOverlay(_screen);
        else
            _overlays.RemoveOverlay(_screen);
    }
}
