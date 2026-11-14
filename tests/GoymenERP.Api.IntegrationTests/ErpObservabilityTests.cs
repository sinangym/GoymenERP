using System.Diagnostics;
using System.Text.Json;
using GoymenERP.Api.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-032 / ERP-000L: the diagnostic file log and the OpenTelemetry exporter are off by default, refuse incomplete
/// configuration, redact before writing, and prune only their own expired daily files under a retention policy.
/// All values below are synthetic.
/// </summary>
public sealed class ErpObservabilityTests
{
    private const string SyntheticPolicyRef = "SENTETIK-retention-policy";

    private static readonly DateTimeOffset FixedNow = new(2026, 11, 14, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_shipped_defaults_resolve_to_no_file_log_and_no_exporter()
    {
        var settings = Bind(new Dictionary<string, string?>
        {
            ["Observability:FileLog:Directory"] = "",
            ["Observability:FileLog:RetentionDays"] = "180",
            ["Observability:FileLog:RetentionPolicyRef"] = SyntheticPolicyRef,
            ["Observability:Otlp:Enabled"] = "false",
            ["Observability:Otlp:Endpoint"] = "",
            ["Observability:Otlp:Protocol"] = "grpc",
        });

        Assert.Null(settings.FileLog);
        Assert.Null(settings.Otlp);
        Assert.Null(new ErpObservabilityOptions().Resolve().FileLog);
        Assert.Null(new ErpObservabilityOptions().Resolve().Otlp);
    }

    [Fact]
    public void A_file_log_without_an_explicit_queue_capacity_fails_instead_of_picking_one()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Bind(new Dictionary<string, string?>
        {
            ["Observability:FileLog:Directory"] = CreateTemporaryDirectory(),
        }));

        Assert.Contains("QueueCapacity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relative_file_log_directory_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Bind(new Dictionary<string, string?>
        {
            ["Observability:FileLog:Directory"] = "logs",
            ["Observability:FileLog:QueueCapacity"] = "16",
        }));

        Assert.Contains("Directory", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("30", "")]
    [InlineData("", SyntheticPolicyRef)]
    [InlineData("0", SyntheticPolicyRef)]
    public void Retention_days_and_the_policy_reference_are_required_together_and_positive(
        string retentionDays,
        string policyRef)
    {
        Assert.Throws<InvalidOperationException>(() => Bind(new Dictionary<string, string?>
        {
            ["Observability:FileLog:Directory"] = CreateTemporaryDirectory(),
            ["Observability:FileLog:QueueCapacity"] = "16",
            ["Observability:FileLog:RetentionDays"] = retentionDays.Length == 0 ? null : retentionDays,
            ["Observability:FileLog:RetentionPolicyRef"] = policyRef,
        }));
    }

    [Theory]
    [InlineData("http://collector.invalid:4317", "grpc", null)]
    [InlineData("http://collector.invalid:4317", "grpc", "1.5")]
    [InlineData("http://collector.invalid:4317", "grpc", "-0.1")]
    [InlineData("http://user:secret@collector.invalid:4317", "grpc", "0.25")]
    [InlineData("http://collector.invalid:4317/?token=x", "grpc", "0.25")]
    [InlineData("ftp://collector.invalid", "grpc", "0.25")]
    [InlineData("", "grpc", "0.25")]
    [InlineData("http://collector.invalid:4317", "thrift", "0.25")]
    public void An_enabled_exporter_with_an_incomplete_or_unsafe_configuration_fails_startup(
        string endpoint,
        string protocol,
        string? samplingRatio)
    {
        Assert.Throws<InvalidOperationException>(() => Bind(new Dictionary<string, string?>
        {
            ["Observability:Otlp:Enabled"] = "true",
            ["Observability:Otlp:Endpoint"] = endpoint,
            ["Observability:Otlp:Protocol"] = protocol,
            ["Observability:Otlp:TraceSamplingRatio"] = samplingRatio,
        }));
    }

    [Fact]
    public void A_metrics_only_exporter_needs_no_sampling_ratio_and_exporting_nothing_is_refused()
    {
        var settings = Bind(new Dictionary<string, string?>
        {
            ["Observability:Otlp:Enabled"] = "true",
            ["Observability:Otlp:Endpoint"] = "http://collector.invalid:4317",
            ["Observability:Otlp:ExportTraces"] = "false",
        });

        Assert.NotNull(settings.Otlp);
        Assert.True(settings.Otlp.ExportMetrics);
        Assert.False(settings.Otlp.ExportTraces);
        Assert.Null(settings.Otlp.TraceSamplingRatio);
        Assert.Equal(OtlpExportProtocol.Grpc, settings.Otlp.Protocol);

        Assert.Throws<InvalidOperationException>(() => Bind(new Dictionary<string, string?>
        {
            ["Observability:Otlp:Enabled"] = "true",
            ["Observability:Otlp:Endpoint"] = "http://collector.invalid:4317",
            ["Observability:Otlp:ExportTraces"] = "false",
            ["Observability:Otlp:ExportMetrics"] = "false",
        }));
    }

    [Fact]
    public void The_http_protobuf_endpoint_gets_the_signal_path_and_grpc_is_used_as_is()
    {
        var http = new ErpOtlpSettings(
            new Uri("http://collector.invalid:4318/"),
            OtlpExportProtocol.HttpProtobuf,
            ExportMetrics: true,
            ExportTraces: true,
            TraceSamplingRatio: 0.5);
        var grpc = http with { Endpoint = new Uri("http://collector.invalid:4317"), Protocol = OtlpExportProtocol.Grpc };

        Assert.Equal(
            "http://collector.invalid:4318/v1/traces",
            ErpObservabilityExtensions.ResolveExporterEndpoint(http, ErpObservabilityExtensions.OtlpTracesPath)
                .AbsoluteUri);
        Assert.Equal(
            "http://collector.invalid:4318/v1/metrics",
            ErpObservabilityExtensions.ResolveExporterEndpoint(http, ErpObservabilityExtensions.OtlpMetricsPath)
                .AbsoluteUri);
        Assert.Equal(
            grpc.Endpoint,
            ErpObservabilityExtensions.ResolveExporterEndpoint(grpc, ErpObservabilityExtensions.OtlpTracesPath));
    }

    [Theory]
    [InlineData("CompanyId", true)]
    [InlineData("ActorUserId", true)]
    [InlineData("RequestPath", true)]
    [InlineData("IdempotencyKey", true)]
    [InlineData("CustomerName", true)]
    [InlineData("CorrelationId", false)]
    [InlineData("ErrorCode", false)]
    [InlineData("ElapsedMilliseconds", false)]
    public void Sensitive_property_names_are_recognised(string name, bool expected)
    {
        Assert.Equal(expected, ErpLogRedaction.IsSensitiveName(name));
    }

    [Fact]
    public void Values_are_redacted_by_name_and_shape_and_objects_are_never_serialized()
    {
        Assert.Equal(ErpLogRedaction.RedactedMarker, ErpLogRedaction.RedactValue("Value", Guid.NewGuid()));
        Assert.Equal(
            ErpLogRedaction.RedactedMarker,
            ErpLogRedaction.RedactValue("Value", Guid.NewGuid().ToString("D")));
        Assert.Equal(ErpLogRedaction.RedactedMarker, ErpLogRedaction.RedactValue("Value", "sentetik@ornek.invalid"));
        Assert.Equal(ErpLogRedaction.RedactedMarker, ErpLogRedaction.RedactValue("CompanyId", 42));
        Assert.Equal(ErpLogRedaction.ObjectMarker, ErpLogRedaction.RedactValue("Value", new { Secret = "x" }));
        Assert.Equal(42, ErpLogRedaction.RedactValue("Count", 42));
        Assert.Equal("BLOCKED", ErpLogRedaction.RedactValue("ErrorCode", "BLOCKED"));

        var cut = Assert.IsType<string>(ErpLogRedaction.RedactValue("Value", new string('a', 1000)));
        Assert.Equal(ErpLogRedaction.MaxStringValueLength, cut.Length);
    }

    [Fact]
    public void A_file_record_keeps_the_template_and_correlation_but_never_the_rendered_message_or_secrets()
    {
        var directory = CreateTemporaryDirectory();
        var companyId = Guid.NewGuid();
        try
        {
            using (var provider = CreateProvider(directory, includeStackTrace: false))
            {
                provider.SetScopeProvider(new LoggerExternalScopeProvider());
                var logger = provider.CreateLogger("SENTETIK.Category");
                using (logger.BeginScope(new Dictionary<string, object?>
                       {
                           ["CorrelationId"] = "sentetik-correlation-1",
                           ["SourceChannel"] = "web",
                           ["RequestPath"] = "/api/v1/sentetik/path",
                       }))
                {
                    logger.LogWarning(
                        new EventId(7, "SentetikEvent"),
                        "Command finished for {CompanyId} with {ErrorCode} after {ElapsedMilliseconds} ms",
                        companyId,
                        "SENTETIK_CODE",
                        12);
                    logger.LogError(
                        new InvalidOperationException("sentetik-exception-message", new TimeoutException("inner")),
                        "Command failed at {Step}",
                        "commit");
                }
            }

            var lines = File.ReadAllLines(Path.Combine(directory, ErpFileLogWriter.FileNameFor(new DateOnly(2026, 11, 14))));
            Assert.Equal(2, lines.Length);
            var raw = string.Join('\n', lines);
            Assert.DoesNotContain(companyId.ToString("D"), raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/api/v1/sentetik/path", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("sentetik-exception-message", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("after 12 ms", raw, StringComparison.Ordinal);

            using var first = JsonDocument.Parse(lines[0]);
            var record = first.RootElement;
            Assert.Equal(ErpFileLoggerProvider.SchemaVersion, record.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("2026-11-14T10:30:00.0000000Z", record.GetProperty("timestamp").GetString());
            Assert.Equal("Warning", record.GetProperty("level").GetString());
            Assert.Equal("SENTETIK.Category", record.GetProperty("category").GetString());
            Assert.Equal(ErpTelemetry.ServiceName, record.GetProperty("service").GetString());
            Assert.Equal(7, record.GetProperty("eventId").GetInt32());
            Assert.Equal("SentetikEvent", record.GetProperty("eventName").GetString());
            Assert.Equal(
                "Command finished for {CompanyId} with {ErrorCode} after {ElapsedMilliseconds} ms",
                record.GetProperty("template").GetString());
            var properties = record.GetProperty("properties");
            Assert.Equal(ErpLogRedaction.RedactedMarker, properties.GetProperty("CompanyId").GetString());
            Assert.Equal("SENTETIK_CODE", properties.GetProperty("ErrorCode").GetString());
            Assert.Equal(12, properties.GetProperty("ElapsedMilliseconds").GetInt64());
            Assert.Equal("sentetik-correlation-1", record.GetProperty("correlationId").GetString());
            Assert.Equal("web", record.GetProperty("sourceChannel").GetString());
            Assert.False(record.TryGetProperty("requestPath", out _));
            Assert.False(record.TryGetProperty("message", out _));

            using var second = JsonDocument.Parse(lines[1]);
            var exception = second.RootElement.GetProperty("exception");
            Assert.Equal(typeof(InvalidOperationException).FullName, exception.GetProperty("type").GetString());
            Assert.Equal(
                typeof(TimeoutException).FullName,
                Assert.Single(exception.GetProperty("innerTypes").EnumerateArray()).GetString());
            Assert.False(exception.TryGetProperty("stackTrace", out _));
            Assert.False(exception.TryGetProperty("message", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Pruning_deletes_only_expired_daily_files_of_this_log_under_a_retention_policy()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var expired = Touch(directory, "goymen-erp-api-20260101.jsonl");
            var kept = Touch(directory, "goymen-erp-api-20261101.jsonl");
            var foreignSuffix = Touch(directory, "goymen-erp-api-20260101.jsonl.bak");
            var foreignName = Touch(directory, "sentetik-20260101.jsonl");
            var malformedDate = Touch(directory, "goymen-erp-api-2026ab01.jsonl");

            var settings = new ErpFileLogSettings(
                directory,
                QueueCapacity: 16,
                RetentionDays: 180,
                RetentionPolicyRef: SyntheticPolicyRef,
                IncludeExceptionStackTrace: false);
            using (var writer = new ErpFileLogWriter(settings, new FixedTimeProvider(FixedNow)))
            {
                writer.Enqueue("{\"schemaVersion\":1}");
            }

            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(kept));
            Assert.True(File.Exists(foreignSuffix));
            Assert.True(File.Exists(foreignName));
            Assert.True(File.Exists(malformedDate));
            Assert.Equal(
                new[] { "{\"schemaVersion\":1}" },
                File.ReadAllLines(Path.Combine(directory, ErpFileLogWriter.FileNameFor(new DateOnly(2026, 11, 14)))));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Without_a_retention_policy_no_log_file_is_ever_deleted()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var old = Touch(directory, "goymen-erp-api-20200101.jsonl");
            var settings = new ErpFileLogSettings(
                directory,
                QueueCapacity: 16,
                RetentionDays: null,
                RetentionPolicyRef: null,
                IncludeExceptionStackTrace: false);
            using (var writer = new ErpFileLogWriter(settings, new FixedTimeProvider(FixedNow)))
            {
                writer.Enqueue("{}");
            }

            Assert.True(File.Exists(old));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void The_trace_processor_keeps_only_allowlisted_tags_and_drops_messages()
    {
        using var activity = new Activity("SENTETIK.Operation");
        activity.SetTag(ErpActivityRedactionProcessor.HttpRequestMethodTag, "POST");
        activity.SetTag(ErpActivityRedactionProcessor.HttpRouteTag, "/api/v1/sentetik/{id}");
        activity.SetTag(ErpActivityRedactionProcessor.CorrelationIdTag, "sentetik-correlation-1");
        activity.SetTag("url.path", "/api/v1/sentetik/123");
        activity.SetTag("url.query", "token=x");
        activity.SetTag("client.address", "192.0.2.10");
        activity.SetTag("user_agent.original", "sentetik-agent");
        activity.SetStatus(ActivityStatusCode.Error, "sentetik-exception-message");
        activity.AddEvent(new ActivityEvent(
            "exception",
            tags: new ActivityTagsCollection
            {
                ["exception.type"] = "System.InvalidOperationException",
                ["exception.message"] = "sentetik-exception-message",
                ["exception.stacktrace"] = "at Sentetik.Frame()",
            }));
        activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;

        using var processor = new ErpActivityRedactionProcessor();
        processor.OnEnd(activity);

        Assert.Equal(
            new[]
            {
                ErpActivityRedactionProcessor.CorrelationIdTag,
                ErpActivityRedactionProcessor.HttpRequestMethodTag,
                ErpActivityRedactionProcessor.HttpRouteTag,
            },
            activity.TagObjects.Select(tag => tag.Key).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("POST /api/v1/sentetik/{id}", activity.DisplayName);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Null(activity.StatusDescription);
        var exceptionEvent = Assert.Single(activity.Events);
        Assert.Equal(
            "exception.type",
            Assert.Single(exceptionEvent.Tags).Key);
        Assert.True(activity.Recorded);
    }

    [Fact]
    public void Nothing_is_registered_while_the_openapi_document_is_generated()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Observability:FileLog:Directory"] = "relative-and-invalid",
            ["Observability:Otlp:Enabled"] = "true",
        });

        var settings = builder.AddErpObservability(isOpenApiDocumentGeneration: true);

        Assert.Null(settings.FileLog);
        Assert.Null(settings.Otlp);
        Assert.Throws<InvalidOperationException>(() => builder.AddErpObservability(isOpenApiDocumentGeneration: false));
    }

    [Fact]
    public async Task A_configured_file_log_is_added_as_a_logger_provider()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:FileLog:Directory"] = directory,
                ["Observability:FileLog:QueueCapacity"] = "16",
            });

            var settings = builder.AddErpObservability(isOpenApiDocumentGeneration: false);

            Assert.NotNull(settings.FileLog);
            Assert.Null(settings.Otlp);
            await using (var app = builder.Build())
            {
                Assert.Contains(
                    app.Services.GetServices<ILoggerProvider>(),
                    provider => provider is ErpFileLoggerProvider);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ErpObservabilitySettings Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new ErpObservabilityOptions();
        configuration.GetSection(ErpObservabilityOptions.SectionName).Bind(options);
        return options.Resolve();
    }

    private static ErpFileLoggerProvider CreateProvider(string directory, bool includeStackTrace)
    {
        return new ErpFileLoggerProvider(
            new ErpFileLogSettings(
                directory,
                QueueCapacity: 64,
                RetentionDays: null,
                RetentionPolicyRef: null,
                IncludeExceptionStackTrace: includeStackTrace),
            "Testing",
            new FixedTimeProvider(FixedNow));
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "goymen-erp-observability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Touch(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
