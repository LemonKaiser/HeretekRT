using Prometheus;
using Content.Shared._WH40K.ClassProgression;

namespace Content.Server._WH40K.ClassProgression;

/// <summary>
/// Aggregate-only release telemetry for bounded Overseer work. No account, character or chat data is recorded.
/// </summary>
public static class Wh40kClassMetrics
{
    private static readonly Histogram CommandRecipients = Metrics.CreateHistogram(
        "wh40k_class_command_recipients",
        "Bodies selected by one bounded Overseer resolver refresh.",
        new HistogramConfiguration
        {
            Buckets = new double[] { 0, 1, 2, 3, 5, 8, 13, 21, 34 },
        });

    private static readonly Histogram CommandCandidates = Metrics.CreateHistogram(
        "wh40k_class_command_candidates",
        "Connected player bodies considered by one Overseer resolver refresh.",
        new HistogramConfiguration
        {
            Buckets = new double[] { 0, 1, 2, 4, 8, 16, 32, 64, 128 },
        });

    private static readonly Histogram CommandRefreshDuration = Metrics.CreateHistogram(
        "wh40k_class_command_refresh_duration_seconds",
        "Wall-clock duration of one bounded Overseer resolver refresh.",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.00005, 2, 14),
        });

    private static readonly Counter NpcPressureApplications = Metrics.CreateCounter(
        "wh40k_class_npc_pressure_applications_total",
        "Accepted non-stacking NPC pressure applications by category.",
        new CounterConfiguration
        {
            LabelNames = new[] { "category" },
        });

    public static void ObserveCommandRefresh(int recipients, int candidates, double durationSeconds)
    {
        CommandRecipients.Observe(recipients);
        CommandCandidates.Observe(candidates);
        CommandRefreshDuration.Observe(durationSeconds);
    }

    public static void ObserveNpcPressure(Wh40kClassModifierCategory category)
    {
        NpcPressureApplications.WithLabels(category.ToString()).Inc();
    }
}
