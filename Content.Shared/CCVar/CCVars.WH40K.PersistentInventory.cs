using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    /// Enables the persistent inventory gameplay integration: save/restore pipeline, cryo, and admin commands.
    /// </summary>
    public static readonly CVarDef<bool> Wh40kPersistentInventoryEnabled =
        CVarDef.Create(
            "wh40k.persistent_inventory.enabled",
            false,
            CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Gradual rollout mode: dry-run, validated, or production.
    /// The master <see cref="Wh40kPersistentInventoryEnabled"/> CVar takes precedence.
    /// </summary>
    public static readonly CVarDef<string> Wh40kPersistentInventoryRolloutMode =
        CVarDef.Create(
            "wh40k.persistent_inventory.rollout_mode",
            "disabled",
            CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Percentage of accounts included in production. Selection is deterministic by UserId.
    /// </summary>
    public static readonly CVarDef<int> Wh40kPersistentInventoryProductionPercentage =
        CVarDef.Create(
            "wh40k.persistent_inventory.production_percentage",
            0,
            CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Logs one aggregate warning per saved profile when unsupported runtime component state is omitted.
    /// </summary>
    public static readonly CVarDef<bool> Wh40kPersistentInventoryWarnOmittedComponents =
        CVarDef.Create(
            "wh40k.persistent_inventory.warn_omitted_components",
            true,
            CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> Wh40kPersistentInventoryMaxRoots =
        CVarDef.Create("wh40k.persistent_inventory.max_roots", 64, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> Wh40kPersistentInventoryMaxEntities =
        CVarDef.Create("wh40k.persistent_inventory.max_entities", 256, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> Wh40kPersistentInventoryMaxDepth =
        CVarDef.Create("wh40k.persistent_inventory.max_depth", 8, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> Wh40kPersistentInventoryMaxComponentsPerEntity =
        CVarDef.Create("wh40k.persistent_inventory.max_components_per_entity", 16, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> Wh40kPersistentInventoryMaxUncompressedBytes =
        CVarDef.Create(
            "wh40k.persistent_inventory.max_uncompressed_bytes",
            4 * 1024 * 1024,
            CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> Wh40kPersistentInventoryMaxCompressedBytes =
        CVarDef.Create(
            "wh40k.persistent_inventory.max_compressed_bytes",
            1024 * 1024,
            CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> Wh40kPersistentInventorySaveCooldownSeconds =
        CVarDef.Create(
            "wh40k.persistent_inventory.save_cooldown_seconds",
            30,
            CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> Wh40kPersistentInventoryMaxConcurrentSaves =
        CVarDef.Create(
            "wh40k.persistent_inventory.max_concurrent_saves",
            4,
            CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Delay after disconnect before deleting the bound body and marking the snapshot as lost.
    /// </summary>
    public static readonly CVarDef<int> Wh40kPersistentInventoryDisconnectDeleteDelaySeconds =
        CVarDef.Create(
            "wh40k.persistent_inventory.disconnect_delete_delay_seconds",
            60 * 60,
            CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> Wh40kPersistentInventoryDatabaseRetrySeconds =
        CVarDef.Create(
            "wh40k.persistent_inventory.database_retry_seconds",
            5,
            CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> Wh40kPersistentInventoryMetricsRefreshSeconds =
        CVarDef.Create(
            "wh40k.persistent_inventory.metrics_refresh_seconds",
            30,
            CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> Wh40kPersistentInventoryShutdownTimeoutSeconds =
        CVarDef.Create(
            "wh40k.persistent_inventory.shutdown_timeout_seconds",
            30,
            CVar.SERVERONLY | CVar.ARCHIVE);
}
