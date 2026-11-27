using Content.Shared._WH40K.Augments.Combat;
using Robust.Client.GameObjects;

namespace Content.Client._WH40K.Augments.Combat;

public sealed class Wh40kHoloDecoyVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<Wh40kHoloDecoyComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var decoy, out var sprite))
        {
            if (decoy.Copied || decoy.Source is not { } sourceUid || !TryComp<SpriteComponent>(sourceUid, out var source))
                continue;

            _sprite.CopySprite((sourceUid, source), (uid, sprite));
            sprite.PostShader = null;
            _sprite.SetVisible((uid, sprite), true);
            decoy.Copied = true;
        }
    }
}
