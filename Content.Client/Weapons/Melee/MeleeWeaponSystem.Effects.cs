using System.Numerics;
using Content.Client._WH40K.Combat;
using Content.Client.Animations;
using Content.Client.Weapons.Melee.Components;
using Content.Shared._WH40K.Weapons.Melee;
using Content.Shared.Item;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.Client.Weapons.Melee;

public sealed partial class MeleeWeaponSystem
{
    private static readonly ProtoId<TagPrototype> KnifeTag = "Knife";
    [Dependency] private SpriteSystem _meleeSprites = default!;
    [Dependency] private TagSystem _meleeTags = default!;

    private const string FadeAnimationKey = "melee-fade";
    private const string SlashAnimationKey = "melee-slash";
    private const string ThrustAnimationKey = "melee-thrust";

    /// <summary>
    /// Does all of the melee effects for a player that are predicted, i.e. character lunge and weapon animation.
    /// </summary>
    public override void DoLunge(EntityUid user, EntityUid playerUid, EntityUid weapon, Angle angle, Vector2 localPos, string? animation, bool predicted = true)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        var lunge = GetLungeAnimation(localPos);

        // Stop any existing lunges on the user.
        _animation.Stop(user, MeleeLungeKey);
        _animation.Play(user, lunge, MeleeLungeKey);

        if (localPos == Vector2.Zero || animation == null)
            return;

        if (!_xformQuery.TryGetComponent(user, out var userXform) || userXform.MapID == MapId.Nullspace)
            return;

        if (user != weapon && TryPlayWeaponMotion(user, weapon, userXform.Coordinates, localPos, angle, animation))
            return;

        var animationUid = SpawnAttachedTo(animation, userXform.Coordinates);

        if (!TryComp<SpriteComponent>(animationUid, out var sprite)
            || !TryComp<WeaponArcVisualsComponent>(animationUid, out var arcComponent))
        {
            return;
        }

        var spriteRotation = Angle.Zero;
        var animationLength = 0.15f;
        const float animationOffset = 1f;
        if (arcComponent.Animation != WeaponArcAnimation.None
            && TryComp(weapon, out MeleeWeaponComponent? meleeWeaponComponent))
        {
            if (user != weapon
                && TryComp(weapon, out SpriteComponent? weaponSpriteComponent))
                _meleeSprites.CopySprite((weapon, weaponSpriteComponent), (animationUid, sprite));

            spriteRotation = meleeWeaponComponent.WideAnimationRotation;

            if (meleeWeaponComponent.SwingLeft)
                angle *= -1;

            // This is presentation only. Combat cooldowns and weapon data stay untouched.
            animationLength = Math.Clamp(0.6f / Math.Max(GetAttackRate(weapon, user, meleeWeaponComponent), 0.1f), 0.08f, 0.6f);
        }
        _meleeSprites.SetRotation((animationUid, sprite), localPos.ToWorldAngle());
        var xform = _xformQuery.GetComponent(animationUid);
        TrackUserComponent track;

        switch (arcComponent.Animation)
        {
            case WeaponArcAnimation.Slash:
                track = EnsureComp<TrackUserComponent>(animationUid);
                track.User = user;
                _animation.Play(animationUid, GetSlashAnimation((animationUid, sprite), angle, spriteRotation, animationLength, animationOffset), SlashAnimationKey);
                if (arcComponent.Fadeout)
                    _animation.Play(animationUid, GetFadeAnimation(sprite, animationLength * 0.5f, animationLength + 0.15f), FadeAnimationKey);
                break;
            case WeaponArcAnimation.Thrust:
                track = EnsureComp<TrackUserComponent>(animationUid);
                track.User = user;
                _animation.Play(animationUid, GetThrustAnimation((animationUid, sprite), animationOffset, spriteRotation, animationLength), ThrustAnimationKey);
                if (arcComponent.Fadeout)
                    _animation.Play(animationUid, GetFadeAnimation(sprite, animationLength * 0.5f, animationLength + 0.15f), FadeAnimationKey);
                break;
            case WeaponArcAnimation.None:
                var (mapPos, mapRot) = TransformSystem.GetWorldPositionRotation(userXform);
                var worldPos = mapPos + (mapRot - userXform.LocalRotation).RotateVec(localPos);
                var newLocalPos = Vector2.Transform(worldPos, TransformSystem.GetInvWorldMatrix(xform.ParentUid));
                TransformSystem.SetLocalPositionNoLerp(animationUid, newLocalPos, xform);
                if (arcComponent.Fadeout)
                    _animation.Play(animationUid, GetFadeAnimation(sprite, 0f, 0.15f), FadeAnimationKey);
                break;
        }
    }

    private bool TryPlayWeaponMotion(EntityUid user, EntityUid weapon, EntityCoordinates origin, Vector2 direction,
        Angle attackAngle, string animation)
    {
        if (animation is not ("WeaponArcPunch" or "WeaponArcSlash" or "WeaponArcThrust") ||
            !TryComp<SpriteComponent>(weapon, out var art) ||
            art.BaseRSI?.Path.CanonPath.StartsWith("/Textures/_WH40K/", StringComparison.Ordinal) != true ||
            !TryComp<ItemComponent>(weapon, out var item) || !TryComp<MeleeWeaponComponent>(weapon, out var melee))
            return false;

        var motion = GetWeaponMotion(weapon, item, melee, animation);
        var attackInterval = 0.72f / Math.Max(GetAttackRate(weapon, user, melee), 0.1f);
        var length = Math.Min(motion.Duration, Math.Max(0.11f, attackInterval));
        var radius = motion.Radius * Math.Clamp(melee.Range / 1.5f, 0.70f, 1.15f);
        if (animation != "WeaponArcSlash")
            radius = Math.Min(radius, Math.Max(0.50f, direction.Length() * 1.13f));
        var sweep = animation == "WeaponArcSlash" && (motion == WH40KMeleeMotion.Blade || motion == WH40KMeleeMotion.Heavy)
            ? Math.Clamp((float) Math.Abs(attackAngle.Degrees) / 75f, 0.8f, 1.28f)
            : 1f;
        if (melee.SwingLeft)
            sweep = -sweep;

        // The left-handed heavy sprites use a sideways grip in the stock arc. Turn the head into contact.
        var gripRotation = motion == WH40KMeleeMotion.Heavy && melee.SwingLeft
            ? melee.WideAnimationRotation + Angle.FromDegrees(90f)
            : melee.WideAnimationRotation;

        // Reuse the local arc entity and follow logic already used for wide attacks.
        var effect = SpawnAttachedTo("WeaponArcSlash", origin);
        var sprite = Comp<SpriteComponent>(effect);
        var target = (effect, sprite);
        _meleeSprites.CopySprite((weapon, art), target);
        _meleeSprites.SetDrawDepth(target, (int) Content.Shared.DrawDepth.DrawDepth.Effects);
        EnsureComp<TrackUserComponent>(effect).User = user;
        Comp<TimedDespawnComponent>(effect).Lifetime = length + 0.06f;

        var facing = direction.ToWorldAngle();
        var rotation = new AnimationTrackComponentProperty
        {
            ComponentType = typeof(SpriteComponent),
            Property = nameof(SpriteComponent.Rotation),
            InterpolationMode = AnimationInterpolationMode.Linear,
        };
        var offset = new AnimationTrackComponentProperty
        {
            ComponentType = typeof(SpriteComponent),
            Property = nameof(SpriteComponent.Offset),
            InterpolationMode = AnimationInterpolationMode.Linear,
        };
        var previous = 0f;
        foreach (var pose in motion.Poses)
        {
            var orbit = facing + Angle.FromDegrees(pose.Orbit * sweep);
            var twist = orbit + gripRotation + Angle.FromDegrees(pose.Tilt * sweep);
            var position = orbit.RotateVec(new Vector2(0f, -pose.Reach * radius));
            var time = (pose.Time - previous) * length;
            rotation.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(twist, time));
            offset.KeyFrames.Add(new AnimationTrackProperty.KeyFrame(position, time));
            if (pose.Time == 0f)
            {
                _meleeSprites.SetRotation(target, twist);
                _meleeSprites.SetOffset(target, position);
            }
            previous = pose.Time;
        }

        _animation.Play(effect, new Animation
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                rotation,
                offset,
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(sprite.Color, length * motion.FadeStart),
                        new AnimationTrackProperty.KeyFrame(sprite.Color.WithAlpha(0f), length * (1f - motion.FadeStart)),
                    },
                },
            },
        }, SlashAnimationKey);
        return true;
    }

    private WH40KMeleeMotion GetWeaponMotion(EntityUid weapon, ItemComponent item, MeleeWeaponComponent melee, string animation)
    {
        if (HasComp<WH40KChainswordComponent>(weapon))
            return WH40KMeleeMotion.Chain;

        var piercing = melee.Damage.DamageDict.GetValueOrDefault("Piercing");
        var slash = melee.Damage.DamageDict.GetValueOrDefault("Slash");
        var blunt = melee.Damage.DamageDict.GetValueOrDefault("Blunt");
        if (_meleeTags.HasTag(weapon, KnifeTag) || (piercing > slash && piercing > blunt) ||
            (animation == "WeaponArcThrust" && melee.Angle == Angle.Zero))
            return WH40KMeleeMotion.Thrust;

        return blunt > slash || item.Size.Id is "Huge" or "Ginormous"
            ? WH40KMeleeMotion.Heavy
            : WH40KMeleeMotion.Blade;
    }

    private Animation GetSlashAnimation(Entity<SpriteComponent> sprite, Angle arc, Angle spriteRotation, float length, float offset)
    {
        var startRotation = sprite.Comp.Rotation + arc * 0.5f;
        var endRotation = sprite.Comp.Rotation - arc * 0.5f;
        var startRotationOffset = startRotation.RotateVec(new Vector2(0f, -offset * 0.9f));
        var middleRotationOffset = sprite.Comp.Rotation.RotateVec(new Vector2(0f, -offset * 1.1f));
        var endRotationOffset = endRotation.RotateVec(new Vector2(0f, -offset * 0.9f));
        startRotation += spriteRotation;
        endRotation += spriteRotation;

        return new Animation()
        {
            Length = TimeSpan.FromSeconds(length + 0.05f),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Rotation),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(startRotation, 0f),
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 0.5f), length * 0.10f),
                        new AnimationTrackProperty.KeyFrame(endRotation, length * 0.15f),
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 0.9f), length * 0.20f),
                        new AnimationTrackProperty.KeyFrame(Angle.Lerp(startRotation, endRotation, 0.8f), length * 0.60f, Easings.OutQuart),
                    }
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(startRotationOffset, 0f),
                        new AnimationTrackProperty.KeyFrame(middleRotationOffset, length * 0.10f),
                        new AnimationTrackProperty.KeyFrame(endRotationOffset, length * 0.15f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Lerp(startRotationOffset, endRotationOffset, 0.8f), length * 0.60f, Easings.OutQuart),
                    }
                },
            }
        };
    }

    private Animation GetThrustAnimation(Entity<SpriteComponent> sprite, float offset, Angle spriteRotation, float length)
    {
        var startOffset = Vector2.Zero;
        var endOffset = sprite.Comp.Rotation.RotateVec(new Vector2(0f, -offset * 1.2f));
        _meleeSprites.SetRotation((sprite.Owner, sprite.Comp), sprite.Comp.Rotation + spriteRotation);

        return new Animation()
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(startOffset, 0f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Lerp(startOffset, endOffset, 0.65f), length * 0.10f),
                        new AnimationTrackProperty.KeyFrame(endOffset, length * 0.20f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Lerp(startOffset, endOffset, 0.9f), length * 0.30f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Lerp(startOffset, endOffset, 0.7f), length * 0.60f, Easings.OutQuart),
                    }
                },
            }
        };
    }

    private Animation GetFadeAnimation(SpriteComponent sprite, float start, float end)
    {
        return new Animation
        {
            Length = TimeSpan.FromSeconds(end),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(sprite.Color, start),
                        new AnimationTrackProperty.KeyFrame(sprite.Color.WithAlpha(0f), end)
                    }
                }
            }
        };
    }

    /// <summary>
    /// Get the sprite offset animation to use for mob lunges.
    /// </summary>
    private Animation GetLungeAnimation(Vector2 direction)
    {
        const float length = 0.1f;

        return new Animation
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, 0f),
                        new AnimationTrackProperty.KeyFrame(direction.Normalized() * 0.15f, length * 0.4f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, length * 0.6f),
                    }
                }
            }
        };
    }

    /// <summary>
    /// Updates the effect positions to follow the user
    /// </summary>
    private void UpdateEffects()
    {
        var query = EntityQueryEnumerator<TrackUserComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var arcComponent, out var xform))
        {
            if (arcComponent.User == null)
                continue;

            if (TerminatingOrDeleted(arcComponent.User.Value) ||
                !_xformQuery.TryGetComponent(arcComponent.User.Value, out var userXform) || userXform.MapID != xform.MapID)
            {
                QueueDel(uid);
                continue;
            }

            var targetPos = TransformSystem.GetWorldPosition(userXform);

            if (arcComponent.Offset != Vector2.Zero)
            {
                var entRotation = TransformSystem.GetWorldRotation(xform);
                targetPos += entRotation.RotateVec(arcComponent.Offset);
            }

            TransformSystem.SetWorldPosition(uid, targetPos);
        }
    }
}
