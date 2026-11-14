using System.Diagnostics.Metrics;
using GoymenERP.Modules.Audit.Application.Contracts;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

/// <summary>
/// Refused attempt alarms (F-023, KIM-D11): low-cardinality counters. Tags are the closed refusal class and the closed
/// failure kind only — never actor, company, command, correlation or problem values.
/// </summary>
public static class RefusedAttemptMetrics
{
    public const string MeterName = "GoymenERP.Audit";
    public const string WriteFailedCounterName = "audit_refused_attempt_write_failed";
    public const string ReplayedCounterName = "audit_refused_attempt_replayed";
    public const string RefusalClassTag = "refusal_class";
    public const string FailureKindTag = "failure_kind";
    public const string UnknownRefusalClass = "unknown";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> WriteFailedCounter = Meter.CreateCounter<long>(WriteFailedCounterName);
    private static readonly Counter<long> ReplayedCounter = Meter.CreateCounter<long>(ReplayedCounterName);

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

    private static string Bounded(string? refusalClass) =>
        RefusedAttemptClassesV1.IsDefined(refusalClass) ? refusalClass! : UnknownRefusalClass;
}
