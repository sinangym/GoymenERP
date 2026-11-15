using System.Diagnostics.Metrics;
using GoymenERP.Modules.Audit.Application.Contracts;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

/// <summary>
/// Refused attempt alarms (F-023, KIM-D11) and volume counters (F023-4): low-cardinality counters. Tags are the closed
/// refusal class and the closed failure kind only — never actor, company, command, correlation or problem values.
/// <see cref="StandaloneObservedCounterName"/> is the measurement KIM-D11 asks for before any aggregation threshold is
/// entered; <see cref="AggregatedCounterName"/> and <see cref="AggregationLostCounterName"/> only move while the
/// aggregation is enabled.
/// </summary>
public static class RefusedAttemptMetrics
{
    public const string MeterName = "GoymenERP.Audit";
    public const string WriteFailedCounterName = "audit_refused_attempt_write_failed";
    public const string ReplayedCounterName = "audit_refused_attempt_replayed";
    public const string StandaloneObservedCounterName = "audit_refused_attempt_standalone_observed";
    public const string AggregatedCounterName = "audit_refused_attempt_aggregated";
    public const string AggregationLostCounterName = "audit_refused_attempt_aggregation_lost";
    public const string RefusalClassTag = "refusal_class";
    public const string FailureKindTag = "failure_kind";
    public const string UnknownRefusalClass = "unknown";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> WriteFailedCounter = Meter.CreateCounter<long>(WriteFailedCounterName);
    private static readonly Counter<long> ReplayedCounter = Meter.CreateCounter<long>(ReplayedCounterName);
    private static readonly Counter<long> StandaloneObservedCounter =
        Meter.CreateCounter<long>(StandaloneObservedCounterName);
    private static readonly Counter<long> AggregatedCounter = Meter.CreateCounter<long>(AggregatedCounterName);
    private static readonly Counter<long> AggregationLostCounter = Meter.CreateCounter<long>(AggregationLostCounterName);

    internal static void RecordWriteFailure(string? refusalClass, string failureKind)
    {
        WriteFailedCounter.Add(
            1,
            new KeyValuePair<string, object?>(RefusalClassTag, Bounded(refusalClass)),
            new KeyValuePair<string, object?>(FailureKindTag, failureKind));
    }

    internal static void RecordReplay(string? refusalClass)
    {
        ReplayedCounter.Add(1, new KeyValuePair<string, object?>(RefusalClassTag, Bounded(refusalClass)));
    }

    /// <summary>A valid, company-checked separate short refusal reached the port (written, folded or failed).</summary>
    internal static void RecordStandaloneObserved(string? refusalClass)
    {
        StandaloneObservedCounter.Add(1, new KeyValuePair<string, object?>(RefusalClassTag, Bounded(refusalClass)));
    }

    /// <summary>A refusal was folded into its window's summary instead of its own row.</summary>
    internal static void RecordAggregated(string? refusalClass)
    {
        AggregatedCounter.Add(1, new KeyValuePair<string, object?>(RefusalClassTag, Bounded(refusalClass)));
    }

    /// <summary>The folded refusals of a summary row that could not be written.</summary>
    internal static void RecordAggregationLost(string? refusalClass, long count)
    {
        if (count <= 0)
        {
            return;
        }

        AggregationLostCounter.Add(count, new KeyValuePair<string, object?>(RefusalClassTag, Bounded(refusalClass)));
    }

    private static string Bounded(string? refusalClass) =>
        RefusedAttemptClassesV1.IsDefined(refusalClass) ? refusalClass! : UnknownRefusalClass;
}
