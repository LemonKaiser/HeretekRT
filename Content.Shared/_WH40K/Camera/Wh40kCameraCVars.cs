using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.Camera;

/// <summary>Camera settings.</summary>
[CVarDefs]
public sealed class Wh40kCameraCVars
{
    /// <summary>
    /// Turns the view to a grid's north on stepping onto it, instead of the nearest cardinal to the old view.
    /// </summary>
    public static readonly CVarDef<bool> DefaultNorth =
        CVarDef.Create("wh40k.camera.default_north", false, CVar.ARCHIVE | CVar.CLIENT | CVar.REPLICATED);
}
