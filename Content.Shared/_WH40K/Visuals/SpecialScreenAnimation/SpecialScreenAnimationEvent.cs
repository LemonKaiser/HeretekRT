using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Visuals.SpecialScreenAnimation;

[Serializable, NetSerializable]
public sealed class PlaySpecialScreenAnimationEvent : EntityEventArgs
{
    public PlaySpecialScreenAnimationEvent(SpecialScreenAnimationData animation)
    {
        Animation = animation;
    }

    public SpecialScreenAnimationData Animation;
}
