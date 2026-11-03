using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    /// Enables automatic removal of disconnected players' bodies, regardless of persistent inventory.
    /// </summary>
    public static readonly CVarDef<bool> Wh40kDisconnectedPlayerCleanupEnabled =
        CVarDef.Create("wh40k.disconnected_player_cleanup.enabled", true, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Time in seconds a player can remain disconnected before their body is removed.
    /// </summary>
    public static readonly CVarDef<int> Wh40kDisconnectedPlayerCleanupDelaySeconds =
        CVarDef.Create("wh40k.disconnected_player_cleanup.delay_seconds", 60 * 60, CVar.SERVERONLY | CVar.ARCHIVE);
}
