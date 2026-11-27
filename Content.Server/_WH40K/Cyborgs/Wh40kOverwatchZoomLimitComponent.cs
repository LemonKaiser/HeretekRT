using System.Numerics;

namespace Content.Server._WH40K.Cyborgs;

/// <summary>
/// Stores a watcher's previous maximum zoom while capped to default zoom on an Overwatch feed.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kOverwatchZoomLimitComponent : Component
{
    [ViewVariables]
    public Vector2 MaxZoom;
}
