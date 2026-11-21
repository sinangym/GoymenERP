using System.Diagnostics.Metrics;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Retention;

/// <summary>
/// Retention purge metrics (OPEN-021 / NS-12). No tags: company, key, command and correlation values never become
/// metric dimensions; the evidence table holds the per-company counts.
/// </summary>
public static class IdempotencyRetentionPurgeMetrics
{
    public const string MeterName = "GoymenERP.Idempotency.Retention";

    public const string PurgedRecordsCounterName = "idempotency_retention_purged_records";
    public const string FailedCyclesCounterName = "idempotency_retention_failed_cycles";
    public const string CycleDurationHistogramName = "idempotency_retention_cycle_duration";

    public const string SecondsUnit = "s";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> PurgedRecords = Meter.CreateCounter<long>(PurgedRecordsCounterName);
    private static readonly Counter<long> FailedCycles = Meter.CreateCounter<long>(FailedCyclesCounterName);
    private static readonly Histogram<double> CycleDuration =
        Meter.CreateHistogram<double>(CycleDurationHistogramName, SecondsUnit);

    internal static void RecordPurged(int count) => PurgedRecords.Add(count);

    internal static void RecordFailedCycle() => FailedCycles.Add(1);

    internal static void RecordCycleDuration(TimeSpan elapsed) => CycleDuration.Record(elapsed.TotalSeconds);
}
