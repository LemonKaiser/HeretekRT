using Content.Shared.Ghost;
using Robust.Shared.Maths;

namespace Content.Server.Ghost;

public sealed partial class GhostSystem
{
    /// <summary>
    ///     Applies a server-authoritative cosmetic tint to an observer ghost.
    /// </summary>
    public void SetGhostDecorationColor(Entity<GhostComponent> ghost, Color color)
    {
        ghost.Comp.Color = color;
        Dirty(ghost);
    }

    /// <summary>
    ///     Restores the normal observer ghost tint after a decoration is removed.
    /// </summary>
    public void RestoreObserverGhostColor(Entity<GhostComponent> ghost)
    {
        ghost.Comp.Color = Color.White;
        Dirty(ghost);
        ApplyAdminOOCColor(ghost.Owner);
    }
}
