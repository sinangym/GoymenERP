using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GoymenERP.Api.Observability;

/// <summary>
/// F-032 / ERP-000L: registers the host's diagnostic telemetry from the <c>Observability</c> section. Nothing is
/// registered while the build-time OpenAPI document is generated, and with the shipped defaults nothing is
/// registered at all: the file log needs a directory and the OTLP exporter needs an explicit opt-in (production
/// activation is a human gate). Logs are never sent over OTLP; they stay in the redacted local file.
/// </summary>
public static class ErpObservabilityExtensions
{
    public const string DeploymentEnvironmentAttribute = "deployment.environment.name";

    public const string AspNetCoreHostingMeterName = "Microsoft.AspNetCore.Hosting";

    public const string RuntimeMeterName = "System.Runtime";

    public const string AspNetCoreActivitySourceName = "Microsoft.AspNetCore";

    public const string HttpServerInstrumentWildcard = "http.server.*";

    public const string OtlpTracesPath = "v1/traces";

    public const string OtlpMetricsPath = "v1/metrics";

    /// <summary>The only tags kept on ASP.NET Core server metrics; the route is a template, never a path.</summary>
    public static readonly string[] HttpServerMetricTagKeys =
    [
        ErpActivityRedactionProcessor.HttpRequestMethodTag,
        ErpActivityRedactionProcessor.HttpRouteTag,
        ErpActivityRedactionProcessor.HttpResponseStatusCodeTag,
        ErpActivityRedactionProcessor.ErrorTypeTag,
    ];

    public static ErpObservabilitySettings AddErpObservability(
        this WebApplicationBuilder builder,
        bool isOpenApiDocumentGeneration)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var disabled = new ErpObservabilitySettings(FileLog: null, Otlp: null);
        if (isOpenApiDocumentGeneration)
        {
            return disabled;
        }

        var options = new ErpObservabilityOptions();
        builder.Configuration.GetSection(ErpObservabilityOptions.SectionName).Bind(options);
        var settings = options.Resolve();
        var environmentName = builder.Environment.EnvironmentName;

        if (settings.FileLog is { } fileLog)
        {
            builder.Logging.Services.AddSingleton<ILoggerProvider>(
                _ => new ErpFileLoggerProvider(fileLog, environmentName, TimeProvider.System));
        }

        if (settings.Otlp is { } otlp)
        {
            AddOtlp(builder.Services, otlp, environmentName);
        }

        return settings;
    }

    private static void AddOtlp(IServiceCollection services, ErpOtlpSettings otlp, string environmentName)
    {
        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(ErpTelemetry.ServiceName)
                .AddAttributes(
                [
                    new KeyValuePair<string, object>(DeploymentEnvironmentAttribute, environmentName),
                ]));

        if (otlp.ExportMetrics)
        {
            telemetry.WithMetrics(metrics => metrics
                .AddMeter(AspNetCoreHostingMeterName, RuntimeMeterName, ErpTelemetry.ModuleMeterWildcard)
                .AddView(
                    HttpServerInstrumentWildcard,
                    new MetricStreamConfiguration { TagKeys = HttpServerMetricTagKeys })
                .AddOtlpExporter(exporter => ConfigureExporter(exporter, otlp, OtlpMetricsPath)));
        }

        if (otlp.ExportTraces)
        {
            telemetry.WithTracing(tracing => tracing
                .SetSampler(new TraceIdRatioBasedSampler(otlp.TraceSamplingRatio!.Value))
                .AddSource(AspNetCoreActivitySourceName, ErpTelemetry.ActivitySourceName)
                // Order matters: the redaction processor must run before the exporter's processor.
                .AddProcessor(new ErpActivityRedactionProcessor())
                .AddOtlpExporter(exporter => ConfigureExporter(exporter, otlp, OtlpTracesPath)));
        }
    }

    /// <summary>
    /// The configured endpoint is the collector base address. gRPC uses it as is; for http/protobuf the signal
    /// path is appended, because an endpoint set in code is not extended by the exporter.
    /// </summary>
    public static Uri ResolveExporterEndpoint(ErpOtlpSettings otlp, string signalPath)
    {
        ArgumentNullException.ThrowIfNull(otlp);
        ArgumentException.ThrowIfNullOrWhiteSpace(signalPath);

        if (otlp.Protocol != OtlpExportProtocol.HttpProtobuf)
        {
            return otlp.Endpoint;
        }

        return new Uri(otlp.Endpoint.AbsoluteUri.TrimEnd('/') + "/" + signalPath, UriKind.Absolute);
    }

    private static void ConfigureExporter(OtlpExporterOptions exporter, ErpOtlpSettings otlp, string signalPath)
    {
        exporter.Protocol = otlp.Protocol;
        exporter.Endpoint = ResolveExporterEndpoint(otlp, signalPath);
    }
}
