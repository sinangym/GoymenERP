using OpenTelemetry.Exporter;

namespace GoymenERP.Api.Observability;

/// <summary>
/// F-032 / ERP-000L: the host's diagnostic telemetry configuration (<c>Observability</c> section).
/// Everything here is diagnostic telemetry only (spec §6.3): it may lose data and is never business audit or a
/// durable security event. Both sinks are OFF unless configuration turns them on:
/// <list type="bullet">
/// <item>the structured file log needs an explicit <see cref="ErpFileLogOptions.Directory"/> and queue capacity;</item>
/// <item>the OTLP exporter (Sinan 2026-10-21 No.134 / EK-44: OpenTelemetry to a local OTLP collector) needs
/// <see cref="ErpOtlpOptions.Enabled"/>, an endpoint and, for traces, an explicit sampling ratio.</item>
/// </list>
/// Capacities and sampling ratios are not chosen in code: they depend on OPEN-014 (inventory) and OPEN-015
/// (volume), so the operator must state them when a sink is turned on, and an incomplete configuration fails
/// startup instead of silently picking a value.
/// </summary>
public sealed class ErpObservabilityOptions
{
    public const string SectionName = "Observability";

    public ErpFileLogOptions FileLog { get; set; } = new();

    public ErpOtlpOptions Otlp { get; set; } = new();

    /// <summary>Validates the bound values and returns the resolved, immutable settings.</summary>
    public ErpObservabilitySettings Resolve()
    {
        return new ErpObservabilitySettings(FileLog.Resolve(), Otlp.Resolve());
    }
}

public sealed class ErpFileLogOptions
{
    /// <summary>Empty (the default) keeps the file log off.</summary>
    public string? Directory { get; set; }

    /// <summary>Bounded in-memory queue between the logger and the file writer. Required when the file log is on.</summary>
    public int? QueueCapacity { get; set; }

    /// <summary>
    /// Days a daily log file is kept. Pruning runs only when both this and <see cref="RetentionPolicyRef"/> are
    /// set: deleting is a retention decision, so it is never done without the policy that allows it.
    /// </summary>
    public int? RetentionDays { get; set; }

    public string? RetentionPolicyRef { get; set; }

    /// <summary>
    /// Off by default: an exception is logged by type chain and HResult only. Messages are never written (they can
    /// carry values); a stack trace is written only when this is turned on.
    /// </summary>
    public bool IncludeExceptionStackTrace { get; set; }

    internal ErpFileLogSettings? Resolve()
    {
        if (string.IsNullOrWhiteSpace(Directory))
        {
            return null;
        }

        var directory = Directory.Trim();
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new InvalidOperationException(
                "Observability:FileLog:Directory mutlak bir klasor yolu olmalidir.");
        }

        if (QueueCapacity is not { } queueCapacity || queueCapacity <= 0)
        {
            throw new InvalidOperationException(
                "Observability:FileLog:QueueCapacity dosya logu acikken pozitif bir deger olarak verilmelidir.");
        }

        var hasRetentionDays = RetentionDays is not null;
        var hasRetentionPolicy = !string.IsNullOrWhiteSpace(RetentionPolicyRef);
        if (hasRetentionDays != hasRetentionPolicy)
        {
            throw new InvalidOperationException(
                "Observability:FileLog:RetentionDays ve RetentionPolicyRef birlikte verilmelidir.");
        }

        if (RetentionDays is { } retentionDays && retentionDays <= 0)
        {
            throw new InvalidOperationException(
                "Observability:FileLog:RetentionDays pozitif olmalidir.");
        }

        return new ErpFileLogSettings(
            directory,
            queueCapacity,
            RetentionDays,
            hasRetentionPolicy ? RetentionPolicyRef!.Trim() : null,
            IncludeExceptionStackTrace);
    }
}

public sealed class ErpOtlpOptions
{
    public const string GrpcProtocol = "grpc";

    public const string HttpProtobufProtocol = "http/protobuf";

    /// <summary>Off by default; turning the exporter on in production is a human gate.</summary>
    public bool Enabled { get; set; }

    public string? Endpoint { get; set; }

    /// <summary><c>grpc</c> or <c>http/protobuf</c>; empty means <c>grpc</c>.</summary>
    public string? Protocol { get; set; }

    public bool ExportMetrics { get; set; } = true;

    public bool ExportTraces { get; set; } = true;

    /// <summary>Head-sampling ratio (0..1) for root traces. Required when traces are exported (OPEN-015).</summary>
    public double? TraceSamplingRatio { get; set; }

    internal ErpOtlpSettings? Resolve()
    {
        if (!Enabled)
        {
            return null;
        }

        if (!ExportMetrics && !ExportTraces)
        {
            throw new InvalidOperationException(
                "Observability:Otlp acikken en az bir sinyal (metrik veya iz) disa aktarilmalidir.");
        }

        if (string.IsNullOrWhiteSpace(Endpoint)
            || !Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new InvalidOperationException(
                "Observability:Otlp:Endpoint kimlik bilgisi, sorgu veya parca icermeyen mutlak bir http/https "
                + "adresi olmalidir.");
        }

        var protocol = (Protocol ?? string.Empty).Trim();
        var exportProtocol = protocol switch
        {
            "" or GrpcProtocol => OtlpExportProtocol.Grpc,
            HttpProtobufProtocol => OtlpExportProtocol.HttpProtobuf,
            _ => throw new InvalidOperationException(
                "Observability:Otlp:Protocol yalniz 'grpc' veya 'http/protobuf' olabilir."),
        };

        double? samplingRatio = null;
        if (ExportTraces)
        {
            if (TraceSamplingRatio is not { } ratio || double.IsNaN(ratio) || ratio < 0d || ratio > 1d)
            {
                throw new InvalidOperationException(
                    "Observability:Otlp:TraceSamplingRatio iz disa aktarimi acikken 0 ile 1 arasinda "
                    + "acikca verilmelidir.");
            }

            samplingRatio = ratio;
        }

        return new ErpOtlpSettings(endpoint, exportProtocol, ExportMetrics, ExportTraces, samplingRatio);
    }
}

public sealed record ErpObservabilitySettings(ErpFileLogSettings? FileLog, ErpOtlpSettings? Otlp);

public sealed record ErpFileLogSettings(
    string Directory,
    int QueueCapacity,
    int? RetentionDays,
    string? RetentionPolicyRef,
    bool IncludeExceptionStackTrace);

public sealed record ErpOtlpSettings(
    Uri Endpoint,
    OtlpExportProtocol Protocol,
    bool ExportMetrics,
    bool ExportTraces,
    double? TraceSamplingRatio);
