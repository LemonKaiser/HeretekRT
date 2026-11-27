using Content.Shared._Rat.Overwatch;
using Robust.Client.Graphics;

namespace Content.Client.Silicons.StationAi;

public sealed partial class StationAiOverlay
{
    /// <summary>
    /// Skips camera static while the Station AI views an Overwatch feed off its core's grid.
    /// </summary>
    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return !_entManager.TryGetComponent(_player.LocalEntity, out RatOverwatchWatchingComponent? watching)
            || watching.Watching == null;
    }
}
