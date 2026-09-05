using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.Visuals.SpecialScreenAnimation;

/// <summary>
/// Server API for sending an intentionally screen-space visual to selected clients.
/// </summary>
public abstract class SharedSpecialScreenAnimationSystem : EntitySystem
{
    public virtual void PlayForPlayer(
        SpriteSpecifier sprite,
        EntityUid player,
        SpecialScreenAnimationData? animation = null,
        string? text = null)
    {
    }

    public virtual void PlayForFilter(
        SpriteSpecifier sprite,
        Filter filter,
        SpecialScreenAnimationData? animation = null,
        string? text = null)
    {
    }
}
