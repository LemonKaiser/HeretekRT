using System;
using System.Numerics;
using Content.Client.Parallax.Data;
using Robust.Shared.Maths;

namespace Content.Client.Parallax;

internal static class ParallaxAnimation
{
    public static int GetFrame(float realTime, ParallaxLayerAnimationConfig animation)
    {
        if (animation.FrameCount <= 1 ||
            animation.FrameColumns <= 0 ||
            animation.FrameSize.X <= 0 ||
            animation.FrameSize.Y <= 0 ||
            animation.CycleInterval <= 0 ||
            animation.FrameDuration <= 0)
        {
            return 0;
        }

        var animationLength = animation.FrameCount * animation.FrameDuration;
        if (animationLength >= animation.CycleInterval)
            return Math.Min((int) (realTime / animation.FrameDuration), animation.FrameCount - 1);

        // Keep the first frame on screen for one full interval before the first pass.
        var cycleTime = (realTime - animation.CycleInterval) % animation.CycleInterval;
        if (realTime < animation.CycleInterval || cycleTime >= animationLength)
            return 0;

        return Math.Min((int) (cycleTime / animation.FrameDuration), animation.FrameCount - 1);
    }

    public static UIBox2 GetFrameRegion(int frame, ParallaxLayerAnimationConfig animation)
    {
        var framePosition = new Vector2(
            frame % animation.FrameColumns * animation.FrameSize.X,
            frame / animation.FrameColumns * animation.FrameSize.Y);

        return UIBox2.FromDimensions(framePosition, animation.FrameSize);
    }
}
