using System.Numerics;
using Content.Shared._WH40K.Bosses;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Client.Atmos.EntitySystems;
using Content.Shared.Eye;
using Content.Shared.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Enums;

namespace Content.Client._WH40K.Bosses;

public sealed class Wh40kChaosMarineSystem : EntitySystem
{
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    private readonly Dictionary<string, Texture> _textures = new();
    private readonly Dictionary<EntityUid, ShaderInstance> _shaders = new();
    private readonly Dictionary<EntityUid, ShaderInstance> _deathShaders = new();
    private readonly Dictionary<EntityUid, ShaderInstance> _weaponShaders = new();
    private readonly Dictionary<(EntityUid Entity, int Side), ShaderInstance> _exhaustShaders = new();
    private ShaderInstance? _impactShader;
    private ShaderInstance? _rocketMarkShader;
    private static readonly ProtoId<ShaderPrototype> WeaponShaderId = "Wh40kMarineWeapon";
    private static readonly ProtoId<ShaderPrototype> ImpactShaderId = "Wh40kMarineImpact";
    private static readonly ProtoId<ShaderPrototype> RocketMarkShaderId = "Wh40kMarineRocketMark";
    private static readonly ProtoId<ShaderPrototype> ExhaustShaderId = "Wh40kMarineExhaust";
    private Wh40kChaosMarineOverlay? _below;
    private Wh40kChaosMarineOverlay? _above;
    private Wh40kChaosMarineWaveOverlay? _waves;
    private Wh40kChaosMarineRocketOverlay? _rocketFloor;
    private Wh40kChaosMarineRocketOverlay? _rocketLocks;

    public override void Initialize()
    {
        _below = new Wh40kChaosMarineBelowOverlay(EntityManager);
        _above = new Wh40kChaosMarineAboveOverlay(EntityManager);
        _overlays.AddOverlay(_below);
        _overlays.AddOverlay(_above);
        _waves = new Wh40kChaosMarineWaveOverlay(EntityManager);
        _overlays.AddOverlay(_waves);
        _rocketFloor = new Wh40kChaosMarineBarrageOverlay(EntityManager);
        _rocketLocks = new Wh40kChaosMarineTargetLockOverlay(EntityManager);
        _overlays.AddOverlay(_rocketFloor);
        _overlays.AddOverlay(_rocketLocks);
        SubscribeLocalEvent<Wh40kChaosMarineComponent, ComponentShutdown>(OnShutdown);
    }

    public Texture Texture(string name)
    {
        if (!_textures.TryGetValue(name, out var texture))
        {
            texture = _resources.GetResource<TextureResource>("/Textures/_WH40K/Mobs/Bosses/ChaosMarine/" + name + ".png").Texture;
            _textures[name] = texture;
        }
        return texture;
    }

    public Texture ChainLinkTexture()
    {
        if (!_textures.TryGetValue("chain_link", out var link))
            _textures["chain_link"] = link = new AtlasTexture(Texture("chain"), new UIBox2(0, 0, 3, 6));
        return link;
    }

    public Texture IdleFrame(string facing, int frame)
    {
        var key = "idle_" + facing + "_" + frame;
        if (_textures.TryGetValue(key, out var texture)) return texture;
        var sheet = Texture("idle_" + facing);
        texture = new AtlasTexture(sheet, UIBox2.FromDimensions(new Vector2(frame % 4 * 64, frame / 4 * 64), new Vector2(64)));
        _textures[key] = texture;
        return texture;
    }

    private void OnShutdown(EntityUid uid, Wh40kChaosMarineComponent component, ComponentShutdown args)
    {
        if (_shaders.Remove(uid, out var shader)) shader.Dispose();
        if (_deathShaders.Remove(uid, out var deathShader)) deathShader.Dispose();
        if (_weaponShaders.Remove(uid, out var weaponShader)) weaponShader.Dispose();
        for (var side = 0; side < 2; side++)
            if (_exhaustShaders.Remove((uid, side), out var exhaust)) exhaust.Dispose();
    }

    public TimeSpan AnimationTime(EntityUid uid) => _timing.CurTime - _metadata.GetPauseTime(uid);

    public bool CanDrawEffects(EntityUid uid, SpriteComponent sprite, TransformComponent xform) =>
        sprite.Visible && !sprite.ContainerOccluded && sprite.Color.A > .01f &&
        (xform.ParentUid == xform.GridUid || xform.ParentUid == xform.MapUid) &&
        (Comp<MetaDataComponent>(uid).VisibilityMask & (int) VisibilityFlags.Normal) != 0 &&
        !(TryComp<StealthComponent>(uid, out var stealth) && stealth.Enabled) &&
        !(TryComp<MobStateComponent>(uid, out var mob) && mob.CurrentState == MobState.Dead);

    public static float AttackCharge(Wh40kChaosMarineComponent state, TimeSpan now)
    {
        if (state.Attack is MarineAttack.None or MarineAttack.Transition or MarineAttack.Summon) return 0;
        var firing = Wh40kChaosMarineCombat.IsRanged(state.Attack) || Wh40kChaosMarineCombat.IsRocket(state.Attack);
        if (firing && state.Shot >= Wh40kChaosMarineCombat.Spec(state.Attack).Shots) return 0;
        var until = firing ? state.NextShotAt : state.ExecuteAt;
        var preparation = MathF.Max(.1f, (float) (state.ExecuteAt - state.StartedAt).TotalSeconds);
        var duration = firing && now >= state.ExecuteAt
            ? MathF.Min(Wh40kChaosMarineCombat.Delay(.55f, state, .1f),
                Wh40kChaosMarineCombat.ShotInterval(state.Attack, state.Shot, state)) : preparation;
        return Math.Clamp(1 - (float) (until - now).TotalSeconds / duration, 0, 1);
    }

    public ShaderInstance WeaponShader(EntityUid uid, Wh40kChaosMarineComponent state, TimeSpan now)
    {
        if (!_weaponShaders.TryGetValue(uid, out var shader))
            _weaponShaders[uid] = shader = _prototypes.Index(WeaponShaderId).InstanceUnique();
        shader.SetParameter("Charge", AttackCharge(state, now));
        shader.SetParameter("Rage", state.PhaseTwo ? .3f + state.HeatStep * .06f : 0);
        shader.SetParameter("EffectTime", (float) now.TotalSeconds);
        return shader;
    }

    public ShaderInstance ImpactShader() => _impactShader ??= _prototypes.Index(ImpactShaderId).InstanceUnique();

    public ShaderInstance RocketMarkShader() => _rocketMarkShader ??= _prototypes.Index(RocketMarkShaderId).InstanceUnique();

    public ShaderInstance ExhaustShader(EntityUid uid, int side, float strength, TimeSpan now)
    {
        if (!_exhaustShaders.TryGetValue((uid, side), out var shader))
        {
            _exhaustShaders[(uid, side)] = shader = _prototypes.Index(ExhaustShaderId).InstanceUnique();
            shader.SetParameter("Seed", Math.Abs(uid.GetHashCode() % 997) * .137f + side * 7.31f);
        }
        shader.SetParameter("Strength", strength);
        shader.SetParameter("EffectTime", (float) now.TotalSeconds);
        return shader;
    }

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<Wh40kChaosMarineComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var state, out var sprite, out var xform))
        {
            var now = AnimationTime(uid);
            var dead = TryComp<MobStateComponent>(uid, out var mob) && mob.CurrentState == MobState.Dead;
            var rotation = _transform.GetWorldRotation(xform);
            var direction = (rotation + _eye.CurrentEye.Rotation).ToWorldVec();
            var facing = Wh40kChaosMarineJumpVisuals.FacingName(
                Wh40kChaosMarineJumpVisuals.Facing(sprite, rotation, _eye.CurrentEye.Rotation));
            var swinging = state.Mace != null || state.Attack == MarineAttack.Punch;
            var prefix = swinging || dead ? "body" : state.Stance == MarineStance.Ranged ? "ranged" : "melee";
            var frame = (int) (now.TotalSeconds * 8) % (facing is "east" or "west" ? 16 : 17);
            sprite.LayerSetTexture(0, prefix == "melee" ? IdleFrame(facing, frame) : Texture(prefix + "_" + facing));
            if (sprite.LayerMapTryGet(FireVisualLayers.Fire, out var fireLayer))
            {
                var heat = state.PhaseTwo && !dead ? Math.Clamp(state.HeatStep, 0, 9) : 0;
                var scale = state.Dying && now >= state.DeathBurnAt ? new Vector2(2.3f, 3.3f) :
                    new Vector2(1.2f + heat * .05f, 1.1f + heat * .1f);
                // Sprite-local metres, measured from the body and Generic_mob_burning frames.
                // Anchor the last opaque fire row to the boots so heat grows upwards.
                var feet = facing is "east" or "west" ? -22 / 32f : -24 / 32f;
                var bodyCenter = facing switch { "east" => -4 / 32f, "west" => 4 / 32f, "south" => -1 / 32f, _ => 0 };
                var flameCenter = facing switch { "east" or "south" => -1 / 32f, "west" => 1 / 32f, _ => 0 };
                sprite.LayerSetScale(fireLayer, scale);
                sprite.LayerSetOffset(fireLayer, new Vector2(bodyCenter - flameCenter * scale.X, feet + 15 / 32f * scale.Y));
            }
            var elapsed = (float) (now - state.StartedAt).TotalSeconds;
            var pulse = state.Attack == MarineAttack.Transition
                ? MathF.Sin(elapsed * 23) * .012f * Wh40kChaosMarineCombat.Smooth((elapsed - 1) / 3) : 0;
            sprite.Offset = new Vector2(pulse, .45f + MathF.Abs(pulse)) +
                            Wh40kChaosMarineWeaponAnimation.BodyOffset(state, now, direction);
            if (state.Dying)
            {
                if (!_deathShaders.TryGetValue(uid, out var deathShader))
                    _deathShaders[uid] = deathShader = _prototypes.Index<ShaderPrototype>("Wh40kMarineDeath").InstanceUnique();
                var duration = Math.Max(.01, (state.DeathEndsAt - state.DeathBurnAt).TotalSeconds);
                deathShader.SetParameter("BurnProgress", (float) Math.Clamp((now - state.DeathBurnAt).TotalSeconds / duration, 0, 1));
                // Only the armour dissolves; the native fire layer must stay bright at 100 stacks.
                sprite.PostShader = null;
                sprite.LayerSetShader(0, deathShader);
                continue;
            }
            if (_deathShaders.Remove(uid, out var oldDeathShader))
            {
                sprite.LayerSetShader(0, (ShaderInstance?) null);
                oldDeathShader.Dispose();
            }
            if (state.PhaseTwo || state.TransitionProtected)
            {
                if (!_shaders.TryGetValue(uid, out var shader))
                {
                    shader = _prototypes.Index<ShaderPrototype>("Wh40kMarineRage").InstanceUnique();
                    _shaders[uid] = shader;
                }
                shader.SetParameter("Rage", state.HeatStep / 9f);
                var phaseTime = Wh40kChaosMarineTransitionVisuals.Elapsed(state, now);
                shader.SetParameter("Transition", !dead && phaseTime >= 0 ? 1f : 0f);
                shader.SetParameter("PhaseTime", MathF.Max(0, phaseTime));
                shader.SetParameter("EffectTime", (float) now.TotalSeconds);
                sprite.PostShader = shader;
            }
            else if (_shaders.Remove(uid, out var shader))
            {
                sprite.PostShader = null;
                shader.Dispose();
            }
        }
    }

    public override void Shutdown()
    {
        if (_below != null) _overlays.RemoveOverlay(_below);
        if (_above != null) _overlays.RemoveOverlay(_above);
        if (_rocketFloor != null) _overlays.RemoveOverlay(_rocketFloor);
        if (_rocketLocks != null) _overlays.RemoveOverlay(_rocketLocks);
        if (_waves != null)
        {
            _overlays.RemoveOverlay(_waves);
            _waves.Dispose();
        }
        foreach (var shader in _shaders.Values) shader.Dispose();
        _shaders.Clear();
        foreach (var shader in _deathShaders.Values) shader.Dispose();
        _deathShaders.Clear();
        foreach (var shader in _weaponShaders.Values) shader.Dispose();
        _weaponShaders.Clear();
        foreach (var shader in _exhaustShaders.Values) shader.Dispose();
        _exhaustShaders.Clear();
        _impactShader?.Dispose();
        _impactShader = null;
        _rocketMarkShader?.Dispose();
        _rocketMarkShader = null;
        _textures.Clear();
        base.Shutdown();
    }
}
