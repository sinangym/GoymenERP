using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GoymenERP.Api.Observability;

/// <summary>
/// F-032: names of the host's own diagnostic instruments. Tags on these instruments are bounded enums only; no
/// tenant, actor, path or free-text value is ever a tag (spec §6.3 cardinality guard).
/// </summary>
public static class ErpTelemetry
{
    public const string ServiceName = "goymen-erp-api";

    public const string MeterName = "GoymenERP.Api.Observability";

    public const string ActivitySourceName = "GoymenERP.Api";

    /// <summary>Prefix of every module meter (for example <c>GoymenERP.SecurityEvents</c>).</summary>
    public const string ModuleMeterWildcard = "GoymenERP.*";

    public const string FileLogDroppedCounterName = "observability_file_log_dropped";

    public const string DropReasonTag = "reason";

    public const string DropReasonQueueFull = "queue_full";

    public const string DropReasonWriteFailed = "write_failed";

    public const string DropReasonShutdown = "shutdown";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> FileLogDropped = Meter.CreateCounter<long>(
        FileLogDroppedCounterName,
        unit: "{record}",
        description: "Diagnostic file log records that were dropped instead of written.");

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    internal static void RecordFileLogDropped(string reason, long count = 1)
    {
        FileLogDropped.Add(count, new KeyValuePair<string, object?>(DropReasonTag, reason));
    }
}
