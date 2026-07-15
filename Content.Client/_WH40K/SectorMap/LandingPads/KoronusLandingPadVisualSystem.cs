using Content.Shared._WH40K.SectorMap.LandingPads;
using Robust.Client.GameObjects;

namespace Content.Client._WH40K.SectorMap.LandingPads;

/// <summary>
/// Hides pad segments below occupied shuttle floor tiles. Uncovered plating remains visible around
/// the landed shuttle, avoiding both z-fighting and the old invisible-pad behaviour.
/// </summary>
public sealed class KoronusLandingPadVisualSystem : VisualizerSystem<KoronusLandingPadVisualComponent>
{
    protected override void OnAppearanceChange(
        EntityUid uid,
        KoronusLandingPadVisualComponent component,
        ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        var covered = AppearanceSystem.TryGetData<bool>(
            uid,
            KoronusLandingPadVisuals.Covered,
            out var value,
            args.Component) && value;
        SpriteSystem.SetVisible((uid, args.Sprite), !covered);
    }
}
