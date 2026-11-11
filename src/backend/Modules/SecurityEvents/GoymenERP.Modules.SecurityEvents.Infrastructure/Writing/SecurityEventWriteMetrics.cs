using System.Diagnostics.Metrics;
using GoymenERP.BuildingBlocks.Security.Events;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;

/// <summary>
/// Security event loss alarm (#66 6806619303 item 11, E6): a low-cardinality counter. Tags are the closed event
/// type, the failure mode and the failure kind only — never actor, company or correlation values.
/// </summary>
public static class SecurityEventWriteMetrics
{
    public const string MeterName = "GoymenERP.SecurityEvents";
    public const string WriteFailureCounterName = "security_event_write_failure";
    public const string EventTypeTag = "event_type";
    public const string FailureModeTag = "failure_mode";
    public const string FailureKindTag = "failure_kind";
    public const string UnknownEventType = "unknown";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> WriteFailureCounter = Meter.CreateCounter<long>(WriteFailureCounterName);

    internal static void RecordWriteFailure(string? eventType, SecurityEventWriteFailureMode failureMode, string failureKind)
    {
        var boundedEventType = SecurityEventTypesV1.IsDefined(eventType) ? eventType! : UnknownEventType;
        WriteFailureCounter.Add(
            1,
            new KeyValuePair<string, object?>(EventTypeTag, boundedEventType),
            new KeyValuePair<string, object?>(FailureModeTag, failureMode.ToString()),
            new KeyValuePair<string, object?>(FailureKindTag, failureKind));
    }
}
