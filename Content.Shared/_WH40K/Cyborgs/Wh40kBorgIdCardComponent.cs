namespace Content.Shared._WH40K.Cyborgs;

/// <summary>
/// Marks the ID card issued to a cyborg so access readers, PDAs, and shipyard consoles
/// treat it as the cyborg's own card even while stowed inside a module.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kBorgIdCardComponent : Component;
