/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._CE.ZLevels.Roof;
using Robust.Client.Graphics;

namespace Content.Client._CE.ZLevels.Roof;

public sealed partial class CEClientZLevelsRoofSystem : CESharedZLevelsRoofSystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new CEZRoofCoverOverlay());
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<CEZRoofCoverOverlay>();
        base.Shutdown();
    }
}
