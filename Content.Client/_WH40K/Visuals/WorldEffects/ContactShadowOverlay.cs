using System.Numerics;
using Content.Shared.Atmos.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Gravity;
using Content.Shared.Item;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Stealth.Components;
using Content.Shared.Standing;
using Content.Shared.SubFloor;
using Content.Shared.Wall;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WH40K.Visuals.WorldEffects;

/// <summary>
/// Procedural contact shadows below mobs and floor-standing objects, below sprites and the normal FOV mask.
/// </summary>
public sealed partial class ContactShadowOverlay : Overlay
{
    private const int MaxShadows = 512;
    private static readonly ProtoId<ShaderPrototype> Shader = "WH40KContactShadow";

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly EntityLookupSystem _lookup;
    private readonly SharedTransformSystem _transform;
    private readonly SpriteSystem _sprites;
    private readonly TurfSystem _turf;
    private readonly SharedGravitySystem _gravity;
    private readonly ShaderInstance _shader;
    private readonly HashSet<EntityUid> _candidates = new();
    private readonly List<Shadow> _shadows = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public ContactShadowOverlay()
    {
        IoCManager.InjectDependencies(this);
        _lookup = _entities.System<EntityLookupSystem>();
        _transform = _entities.System<SharedTransformSystem>();
        _sprites = _entities.System<SpriteSystem>();
        _turf = _entities.System<TurfSystem>();
        _gravity = _entities.System<SharedGravitySystem>();
        _shader = _prototypes.Index(Shader).InstanceUnique();
        // Above floor details, below every mob draw depth.
        ZIndex = (int) DrawDepth.Puddles + 1;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace || args.Viewport.Eye is not { } eye)
            return;

        _candidates.Clear();
        _shadows.Clear();
        _lookup.GetEntitiesIntersecting(args.MapId, args.WorldAABB.Enlarged(1f), _candidates,
            LookupFlags.Uncontained | LookupFlags.Approximate);

        foreach (var uid in _candidates)
        {
            if (!_entities.TryGetComponent<SpriteComponent>(uid, out var sprite) ||
                !sprite.Visible || sprite.ContainerOccluded || sprite.Color.A <= 0.01f ||
                !_entities.TryGetComponent<TransformComponent>(uid, out var xform) ||
                xform.MapID != args.MapId ||
                (xform.ParentUid != xform.GridUid && xform.ParentUid != xform.MapUid) ||
                _entities.GetComponent<MetaDataComponent>(uid).VisibilityMask != 1 ||
                _entities.TryGetComponent<StealthComponent>(uid, out var stealth) && stealth.Enabled)
                continue;

            var isMob = _entities.HasComponent<MobStateComponent>(uid);
            var isGasTank = _entities.HasComponent<GasTankComponent>(uid);
            var isFloorObject = _entities.HasComponent<PullableComponent>(uid) &&
                                !_entities.HasComponent<ItemComponent>(uid);

            if (!isMob && !isFloorObject && !isGasTank)
                continue;

            // Pullable is shared by nearly every floor structure, including some things that are actually part of
            // the wall or floor. Keep only objects that can visually stand on the tile.
            if (!isMob && (_entities.HasComponent<DoorComponent>(uid) ||
                           _entities.HasComponent<WallMountComponent>(uid) ||
                           _entities.HasComponent<IsRoofComponent>(uid) ||
                           _entities.HasComponent<SubFloorHideComponent>(uid)))
                continue;

            if ((!xform.Anchored && _gravity.IsWeightless(uid)) ||
                (isMob && _entities.TryGetComponent<BuckleComponent>(uid, out var buckle) && buckle.Buckled) ||
                !_turf.TryGetTileRef(xform.Coordinates, out var tile) || _turf.IsSpace(tile.Value))
                continue;

            var bounds = _sprites.GetLocalBounds((uid, sprite));
            if (bounds.Width <= 0f || bounds.Height <= 0f)
                continue;

            var lying = isMob && _entities.TryGetComponent<StandingStateComponent>(uid, out var standing) &&
                        standing.CurrentState is StandingState.Lying or StandingState.GettingUp;
            var position = _transform.GetWorldPosition(xform);
            var worldRotation = _transform.GetWorldRotation(xform);
            var spritePosition = position + (sprite.NoRotation
                ? (-eye.Rotation).RotateVec(sprite.Offset)
                : worldRotation.RotateVec(sprite.Offset));
            var spriteRotation = sprite.NoRotation
                ? sprite.Rotation - eye.Rotation
                : sprite.Rotation + worldRotation;

            Vector2 center;
            Vector2 size;
            Angle shadowRotation;
            if (lying)
            {
                // A prone mob is made by rotating an otherwise upright sprite by 90 degrees. The body's long axis
                // therefore comes from the sprite's local Y axis, not its local X axis. Keep the shadow centered
                // below the complete body and rotate its long X axis onto that body axis.
                var length = Math.Clamp(bounds.Height * 0.94f, 0.58f, 1.65f);
                var thickness = Math.Clamp(bounds.Width * 0.28f, 0.16f, 0.48f);
                var bodyCenter = spritePosition + spriteRotation.RotateVec(bounds.Center);
                var screenDown = (-eye.Rotation).RotateVec(-Vector2.UnitY);
                var spriteRight = spriteRotation.RotateVec(Vector2.UnitX);
                var spriteUp = spriteRotation.RotateVec(Vector2.UnitY);
                var bottomExtent = MathF.Abs(Vector2.Dot(spriteRight, screenDown)) * bounds.Width * 0.5f +
                                   MathF.Abs(Vector2.Dot(spriteUp, screenDown)) * bounds.Height * 0.5f;

                // Sit at the bottom of the rotated texture, but pull the ellipse slightly back into the silhouette.
                center = bodyCenter + screenDown * MathF.Max(0f, bottomExtent - thickness * 0.35f);
                size = new Vector2(length, thickness);
                shadowRotation = spriteRotation + Angle.FromDegrees(90);
            }
            else if (!isMob)
            {
                var widthScale = isGasTank ? 0.48f : 0.74f;
                var maximumWidth = isGasTank ? 0.58f : 1.8f;
                var width = Math.Clamp(bounds.Width * widthScale, 0.24f, maximumWidth);
                var height = Math.Clamp(bounds.Height * (isGasTank ? 0.12f : 0.15f), 0.1f, 0.3f);
                var localCenter = new Vector2(bounds.Center.X, bounds.Bottom - height * 0.04f);

                center = spritePosition + spriteRotation.RotateVec(localCenter);
                size = new Vector2(width, height);
                shadowRotation = spriteRotation;
            }
            else
            {
                var width = Math.Clamp(bounds.Width * 0.62f, 0.32f, 1.15f);
                var height = Math.Clamp(bounds.Height * 0.17f, 0.12f, 0.24f);
                var localCenter = new Vector2(bounds.Center.X, bounds.Bottom - height * 0.08f);

                center = spritePosition + spriteRotation.RotateVec(localCenter);
                size = new Vector2(width, height);
                shadowRotation = spriteRotation;
            }

            _shadows.Add(new Shadow(uid, center, size, shadowRotation,
                sprite.Color.A * (lying ? 0.42f : isMob ? 0.58f : isGasTank ? 0.38f : 0.48f),
                Vector2.DistanceSquared(position, args.WorldAABB.Center)));
        }

        // Stable selection when a busy viewport exceeds the draw budget.
        if (_shadows.Count > MaxShadows)
        {
            _shadows.Sort(static (a, b) =>
            {
                var distance = a.Distance.CompareTo(b.Distance);
                return distance != 0 ? distance : a.Entity.CompareTo(b.Entity);
            });
        }

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        for (var i = 0; i < Math.Min(MaxShadows, _shadows.Count); i++)
        {
            var shadow = _shadows[i];
            // Keep one transform/shader for the whole batch of quads.
            var rect = new Box2Rotated(Box2.CenteredAround(shadow.Center, shadow.Size), shadow.Rotation, shadow.Center);
            handle.DrawRect(rect, Color.Black.WithAlpha(shadow.Alpha));
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        _shader.Dispose();
        base.DisposeBehavior();
    }

    private readonly record struct Shadow(EntityUid Entity, Vector2 Center, Vector2 Size, Angle Rotation, float Alpha, float Distance);
}
