using System.Numerics;
using Content.Client.Hands.Systems;
using Content.Client.Light.Components;
using Content.Client.Particles;
using Content.Shared._WH40K.ClassProgression;
using Content.Shared._WH40K.Visuals;
using Content.Shared.Particles;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Visuals.WorldEffects;

public sealed partial class WH40KBallisticMuzzleSystem : EntitySystem
{
    private static readonly ShotEffects Pistol = new("HrtMuzzleFlashPistol", "HrtMuzzleGasPistol");
    private static readonly ShotEffects Automatic = new("HrtMuzzleFlashBallistic", "HrtMuzzleSmoke");
    private static readonly ShotEffects MachineGun = new("HrtMuzzleFlashMachineGun", "HrtMuzzleGasMachineGun");
    private static readonly TimeSpan GasInterval = TimeSpan.FromMilliseconds(125);
    private static readonly Color MuzzleLightColor = Color.FromHex("#ffa223");
    private const float MuzzleLightDuration = 0.12f;

    [Dependency] private ParticleSystem _particles = default!;
    [Dependency] private PointLightSystem _lights = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private RsiVisualGeometryCache _geometry = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly Dictionary<EntityUid, ShotTiming> _recentShots = new();
    private readonly Dictionary<EntityPrototype, WH40KMuzzleProfile?> _inheritedProfiles = new();
    private readonly List<EntityUid> _expired = new();
    private TimeSpan _nextCleanup;

    public void ShowShot(EntityUid gun, EntityUid? holder, Angle angle, bool compact)
    {
        if (!TryComp(gun, out TransformComponent? gunTransform) || gunTransform.MapID == MapId.Nullspace)
            return;

        var now = _timing.CurTime;
        var previous = _recentShots.TryGetValue(gun, out var shot);
        if (previous && shot.LastFlash == now)
            return;

        var direction = angle.ToVec();
        var muzzle = GetMuzzlePosition(gun, gunTransform, holder, direction);
        SpawnMuzzleLight(muzzle);

        var profile = GetProfile(gun, compact);
        var effects = profile switch
        {
            WH40KMuzzleProfile.Pistol => Pistol,
            WH40KMuzzleProfile.MachineGun => MachineGun,
            _ => Automatic,
        };

        // The particle angle convention starts at +Y and grows toward +X.
        var emission = new Angle(Math.Atan2(direction.X, direction.Y));
        var intensity = compact && profile != WH40KMuzzleProfile.Pistol ? 0.81f : 1f;
        var parameters = new ParticleSpawnParameters(EmitAngle: emission, Intensity: intensity);
        _particles.SpawnEffect(effects.Flash, muzzle, parameters);

        var gasAt = shot.GasAt;
        if (!previous || now >= gasAt)
        {
            _particles.SpawnEffect(effects.Gas, muzzle.Offset(direction * 0.027f), parameters);
            gasAt = now + GasInterval;
        }
        _recentShots[gun] = new ShotTiming(now, gasAt);
    }

    private void SpawnMuzzleLight(MapCoordinates muzzle)
    {
        var uid = Spawn(null, muzzle);
        var light = _lights.EnsureLight(uid);
        _lights.SetColor(uid, MuzzleLightColor, light);
        _lights.SetRadius(uid, 1.4f, light);
        _lights.SetEnergy(uid, 5f, light);
        AddComp(uid, new LightFadeComponent { Duration = MuzzleLightDuration });
        AddComp(uid, new TimedDespawnComponent { Lifetime = MuzzleLightDuration });
    }

    private WH40KMuzzleProfile GetProfile(EntityUid gun, bool compact)
    {
        if (TryComp<WH40KMuzzleProfileComponent>(gun, out var visual))
            return visual.Profile;

        if (TryComp<Wh40kWeaponHandlingComponent>(gun, out var handling))
        {
            return handling.Category switch
            {
                Wh40kWeaponHandlingCategory.Pistol => WH40KMuzzleProfile.Pistol,
                Wh40kWeaponHandlingCategory.LightMachineGun or Wh40kWeaponHandlingCategory.HeavyMachineGun => WH40KMuzzleProfile.MachineGun,
                _ => WH40KMuzzleProfile.Automatic,
            };
        }

        var fallback = compact ? WH40KMuzzleProfile.Pistol : WH40KMuzzleProfile.Automatic;
        if (MetaData(gun).EntityPrototype is not { } prototype)
            return fallback;

        if (!_inheritedProfiles.TryGetValue(prototype, out var profile))
        {
            foreach (var (parent, _) in _prototypes.EnumerateAllParents<EntityPrototype>(prototype.ID))
            {
                profile = parent switch
                {
                    "BaseWeaponPistol" or "BaseWeaponRevolver" => WH40KMuzzleProfile.Pistol,
                    "BaseWeaponLightMachineGun" or "BaseWeaponHeavyMachineGun" => WH40KMuzzleProfile.MachineGun,
                    "BaseWeaponSubMachineGun" => WH40KMuzzleProfile.Automatic,
                    _ => null,
                };
                if (profile != null)
                    break;
            }
            _inheritedProfiles.Add(prototype, profile);
        }

        return profile ?? fallback;
    }

    private MapCoordinates GetMuzzlePosition(EntityUid gun, TransformComponent gunTransform, EntityUid? holder, Vector2 direction)
    {
        var coordinates = _transform.GetMapCoordinates(gunTransform);
        var eye = _eye.CurrentEye.Rotation;
        var aimOnScreen = eye.RotateVec(direction);
        var axis = MathF.Abs(aimOnScreen.X) >= MathF.Abs(aimOnScreen.Y)
            ? new Vector2(MathF.Sign(aimOnScreen.X), 0f)
            : new Vector2(0f, MathF.Sign(aimOnScreen.Y));
        var forward = (-eye).RotateVec(axis);
        var across = new Vector2(-forward.Y, forward.X);
        var bounds = new ContactShadowProjectedBounds();

        if (holder is { } carrier && _hands.GetHeldVisualLayers(carrier, gun) is { } layers &&
            TryComp<SpriteComponent>(carrier, out var carrierSprite) && TryComp(carrier, out TransformComponent? carrierTransform))
        {
            var (position, rotation) = _transform.GetWorldPositionRotation(carrierTransform);
            var screenAngle = (rotation + eye).Reduced().FlipPositive();
            foreach (var key in layers)
            {
                if (_sprites.TryGetLayer((carrier, carrierSprite), key, out var layer, logMissing: false))
                    SpriteLayerWorldTransform.IncludeOpaqueLayer(_geometry, carrierSprite, layer, position, rotation,
                        screenAngle, eye, across, forward, ref bounds);
            }
        }
        else if (TryComp<SpriteComponent>(gun, out var gunSprite) &&
                 _sprites.TryGetLayer((gun, gunSprite), 0, out var baseLayer, logMissing: false))
        {
            var (position, rotation) = _transform.GetWorldPositionRotation(gunTransform);
            SpriteLayerWorldTransform.IncludeOpaqueLayer(_geometry, gunSprite, baseLayer, position, rotation,
                (rotation + eye).Reduced().FlipPositive(), eye, across, forward, ref bounds);
        }

        if (!bounds.Valid)
            return coordinates.Offset(direction * 0.35f);

        return new MapCoordinates(across * bounds.CenterX + forward * bounds.MaxY, coordinates.MapId);
    }

    public override void FrameUpdate(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _nextCleanup)
            return;

        _nextCleanup = now + TimeSpan.FromSeconds(1);
        foreach (var (gun, shot) in _recentShots)
        {
            if (now >= shot.GasAt || TerminatingOrDeleted(gun))
                _expired.Add(gun);
        }

        foreach (var gun in _expired)
            _recentShots.Remove(gun);
        _expired.Clear();
    }

    private readonly record struct ShotEffects(ProtoId<ParticleEffectPrototype> Flash, ProtoId<ParticleEffectPrototype> Gas);
    private readonly record struct ShotTiming(TimeSpan LastFlash, TimeSpan GasAt);
}
