using Content.Server.Database;
using Prometheus;

namespace Content.Server._WH40K.PersistentInventory;

public static class PersistentInventoryMetrics
{
    public static readonly Gauge StateCount = Metrics.CreateGauge(
        "wh40k_persistent_inventory_state_count",
        "Durable persistent inventory accounts by state.",
        "state");

    public static readonly Gauge RolloutMode = Metrics.CreateGauge(
        "wh40k_persistent_inventory_rollout_mode",
        "Current rollout mode; exactly one label is 1.",
        "mode");

    public static readonly Gauge ActiveSaves = Metrics.CreateGauge(
        "wh40k_persistent_inventory_active_saves",
        "Persistent inventory save and dry-run operations currently holding a global slot.");

    public static readonly Histogram PhaseDuration = Metrics.CreateHistogram(
        "wh40k_persistent_inventory_phase_duration_seconds",
        "Persistent inventory phase duration in seconds.",
        new HistogramConfiguration
        {
            LabelNames = new[] { "operation", "phase" },
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 16),
        });

    public static readonly Histogram LockDuration = Metrics.CreateHistogram(
        "wh40k_persistent_inventory_lock_duration_seconds",
        "Duration of account and world locks.",
        new HistogramConfiguration
        {
            LabelNames = new[] { "operation" },
            Buckets = Histogram.ExponentialBuckets(0.01, 2, 16),
        });

    public static readonly Counter DatabaseOperations = Metrics.CreateCounter(
        "wh40k_persistent_inventory_database_operations_total",
        "Persistent inventory database operations and retries.",
        "operation",
        "result");

    public static readonly Counter ValidationFailures = Metrics.CreateCounter(
        "wh40k_persistent_inventory_validation_failures_total",
        "Persistent inventory policy, hash, limit, schema and migration failures.",
        "operation",
        "reason");

    public static readonly Counter MigrationActions = Metrics.CreateCounter(
        "wh40k_persistent_inventory_migration_actions_total",
        "Applied persistent inventory migration actions.",
        "kind");

    public static void SetStateCounts(IReadOnlyList<PersistentInventoryStateCount> counts)
    {
        foreach (var state in Enum.GetValues<PersistentInventorySnapshotState>())
            StateCount.WithLabels(state.ToString()).Set(0);
        foreach (var entry in counts)
            StateCount.WithLabels(entry.State.ToString()).Set(entry.Count);
    }

    public static void SetRolloutMode(PersistentInventoryRolloutMode mode)
    {
        foreach (var candidate in Enum.GetValues<PersistentInventoryRolloutMode>())
            RolloutMode.WithLabels(candidate.ToString()).Set(candidate == mode ? 1 : 0);
    }

    public static void ObserveMigrationActions(IReadOnlyList<string>? actions)
    {
        if (actions == null)
            return;

        foreach (var action in actions)
        {
            var separator = action.IndexOf(':');
            var kind = separator > 0 ? action[..separator] : "unknown";
            MigrationActions.WithLabels(kind).Inc();
        }
    }
}
