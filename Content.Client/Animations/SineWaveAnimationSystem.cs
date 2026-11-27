using System.Numerics;
using Content.Shared.Animation;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Random;

namespace Content.Client.Animations;

public sealed partial class SineWaveAnimationSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _animationPlayer = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string SineWaveAnimationKey = "sinWave";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SineWaveAnimationComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SineWaveAnimationComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SineWaveAnimationComponent, AnimationCompletedEvent>(OnAnimationCompleted);
    }

    private void OnStartup(Entity<SineWaveAnimationComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        ent.Comp.StartOffset = sprite.Offset;
        ent.Comp.StartRotation = sprite.Rotation;

        var animationPlayer = EnsureComp<AnimationPlayerComponent>(ent);
        _animationPlayer.Play((ent, animationPlayer), GetAnimation(ent.Comp, sprite), SineWaveAnimationKey);
    }

    private void OnShutdown(Entity<SineWaveAnimationComponent> ent, ref ComponentShutdown args)
    {
        TryResetFields(ent);

        if (TryComp<AnimationPlayerComponent>(ent, out var animationPlayer))
            _animationPlayer.Stop(ent, animationPlayer, SineWaveAnimationKey);
    }

    private void OnAnimationCompleted(EntityUid uid, SineWaveAnimationComponent comp, AnimationCompletedEvent args)
    {
        if (args.Key != SineWaveAnimationKey || !args.Finished || !comp.Repeat)
        {
            TryResetFields((uid, comp));
            return;
        }

        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        _animationPlayer.Play((uid, args.AnimationPlayer), GetAnimation(comp, sprite), SineWaveAnimationKey);
    }

    private Animation GetAnimation(SineWaveAnimationComponent sineComp, SpriteComponent sprite)
    {
        if (sineComp is { TotalTimeX.TotalSeconds: 0, XWave: not null })
        {
            sineComp.TotalTimeX = sineComp.XWave.Value.PhaseOffset ??
                                  TimeSpan.FromSeconds(_random.NextDouble(0, sineComp.XWave.Value.Period.TotalSeconds));
        }

        if (sineComp is { TotalTimeY.TotalSeconds: 0, YWave: not null })
        {
            sineComp.TotalTimeY = sineComp.YWave.Value.PhaseOffset ??
                                  TimeSpan.FromSeconds(_random.NextDouble(0, sineComp.YWave.Value.Period.TotalSeconds));
        }

        var rotationKeyFrames = new List<AnimationTrackProperty.KeyFrame>();
        var offsetKeyFrames = new List<AnimationTrackProperty.KeyFrame>();

        rotationKeyFrames.Add(new AnimationTrackProperty.KeyFrame(sprite.Rotation, 0f));
        offsetKeyFrames.Add(new AnimationTrackProperty.KeyFrame(sprite.Offset, 0f));

        var keyFrames = Math.Max(1, (int) sineComp.KeyFrames);
        var stepValue = sineComp.AnimationLength / keyFrames;

        for (var i = 1; i <= keyFrames; i++)
        {
            var currTime = stepValue * i;

            var offset = Vector2.Zero;
            var rotation = Angle.Zero;

            if (sineComp.XWave is { } xWave)
            {
                var a = (currTime + sineComp.TotalTimeX) / xWave.Period;
                offset.X = (float) (xWave.Amplitude * Math.Sin(a));

                if (xWave.RotateToFollowSlope)
                    rotation += new Angle(Math.Atan(Math.Cos(a)));
            }

            if (sineComp.YWave is { } yWave)
            {
                var a = (currTime + sineComp.TotalTimeY) / yWave.Period;
                offset.Y = (float) (yWave.Amplitude * Math.Cos(a));

                if (yWave.RotateToFollowSlope)
                    rotation += new Angle(Math.Atan(-Math.Sin(a)));
            }

            rotationKeyFrames.Add(new AnimationTrackProperty.KeyFrame(rotation, (float) stepValue.TotalSeconds));
            offsetKeyFrames.Add(new AnimationTrackProperty.KeyFrame(offset, (float) stepValue.TotalSeconds));
        }

        sineComp.TotalTimeX += sineComp.AnimationLength;
        sineComp.TotalTimeY += sineComp.AnimationLength;

        return new Animation
        {
            Length = sineComp.AnimationLength,
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Rotation),
                    KeyFrames = rotationKeyFrames,
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    KeyFrames = offsetKeyFrames,
                },
            },
        };
    }

    private void TryResetFields(Entity<SineWaveAnimationComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (ent.Comp.ResetOffsetOnEnd)
            _sprite.SetOffset((ent.Owner, sprite), ent.Comp.StartOffset);

        if (ent.Comp.ResetRotationOnEnd)
            _sprite.SetRotation((ent.Owner, sprite), ent.Comp.StartRotation);
    }
}
