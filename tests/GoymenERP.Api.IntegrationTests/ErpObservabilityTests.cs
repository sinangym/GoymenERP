using System.Diagnostics;
using System.Text.Json;
using GoymenERP.Api.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-032 / ERP-000L: the diagnostic file log and the OpenTelemetry exporter are off by default, refuse incomplete
/// configuration, redact before writing, and prune only their own expired daily files under a retention policy.
/// KLN-D51: a full file log queue drops the oldest information records, keeps errors and reports the dropped count;
/// a configured share of traces is sampled and failed requests are always traced. KLN-D52: a file log is never
/// turned on without a retention. All values below are synthetic.
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

    [Fact]
    public void A_file_log_without_a_retention_fails_startup_so_local_logs_are_always_pruned()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Bind(new Dictionary<string, string?>
        {
            ["Observability:FileLog:Directory"] = CreateTemporaryDirectory(),
            ["Observability:FileLog:QueueCapacity"] = "16",
        }));

        Assert.Contains("RetentionDays", exception.Message, StringComparison.Ordinal);
        Assert.Contains("RetentionPolicyRef", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_kln_d51_and_kln_d52_starting_values_resolve_from_settings()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            // The same keys and values as the shipped appsettings.json, with the sinks turned on.
            var settings = Bind(new Dictionary<string, string?>
            {
                ["Observability:FileLog:Directory"] = directory,
                ["Observability:FileLog:QueueCapacity"] = "10000",
                ["Observability:FileLog:RetentionDays"] = "180",
                ["Observability:FileLog:RetentionPolicyRef"] = "KLN-D52",
                ["Observability:Otlp:Enabled"] = "true",
                ["Observability:Otlp:Endpoint"] = "http://collector.invalid:4317",
                ["Observability:Otlp:Protocol"] = "grpc",
                ["Observability:Otlp:TraceSamplingRatio"] = "0.1",
            });

            Assert.NotNull(settings.FileLog);
            Assert.Equal(10000, settings.FileLog.QueueCapacity);
            Assert.Equal(180, settings.FileLog.RetentionDays);
            Assert.Equal("KLN-D52", settings.FileLog.RetentionPolicyRef);
            Assert.NotNull(settings.Otlp);
            Assert.Equal(0.1, settings.Otlp.TraceSamplingRatio);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
    [InlineData("AdminPassword", true)]
    [InlineData("DbPasswd", true)]
    [InlineData("CorrelationId", false)]
    [InlineData("ErrorCode", false)]
    [InlineData("ElapsedMilliseconds", false)]
    public void Sensitive_property_names_are_recognised(string name, bool expected)
    {
        Assert.Equal(expected, ErpLogRedaction.IsSensitiveName(name));
    }

    [Fact]
    public void Exception_types_are_named_without_reflection_by_their_nearest_listed_type()
    {
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            ErpFileLoggerProvider.DescribeExceptionType(new InvalidOperationException()));
        Assert.Equal(
            typeof(ObjectDisposedException).FullName,
            ErpFileLoggerProvider.DescribeExceptionType(new ObjectDisposedException("sentetik")));
        Assert.Equal(
            typeof(ArgumentNullException).FullName,
            ErpFileLoggerProvider.DescribeExceptionType(new ArgumentNullException("sentetik")));
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            ErpFileLoggerProvider.DescribeExceptionType(new SentetikInvalidOperationException()));
        Assert.Equal(
            typeof(Exception).FullName,
            ErpFileLoggerProvider.DescribeExceptionType(new SentetikException()));
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
        activity.AddEvent(new ActivityEvent("sentetik-event-without-tags"));
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
        Assert.Empty(Assert.Single(activity.Events).Tags);
        Assert.True(activity.Recorded);
    }

    [Fact]
    public void A_span_whose_event_carries_a_message_is_not_exported()
    {
        // Event tags cannot be edited once the event is on the activity, so an event that carries anything is
        // not cleaned: the span is dropped (fail closed) instead of exporting the exception message.
        using var activity = new Activity("SENTETIK.Operation");
        activity.SetTag(ErpActivityRedactionProcessor.HttpRequestMethodTag, "POST");
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

        Assert.False(activity.Recorded);
        Assert.False(activity.IsAllDataRequested);
    }

    [Fact]
    public void A_full_file_log_queue_drops_the_oldest_information_record_and_keeps_errors()
    {
        var queue = new ErpFileLogQueue(capacity: 3);
        Assert.Equal(new ErpFileLogEnqueueResult(true, null), queue.Enqueue(LogLevel.Information, "i1"));
        Assert.Equal(new ErpFileLogEnqueueResult(true, null), queue.Enqueue(LogLevel.Error, "e1"));
        Assert.Equal(new ErpFileLogEnqueueResult(true, null), queue.Enqueue(LogLevel.Debug, "i2"));

        Assert.Equal(
            new ErpFileLogEnqueueResult(true, ErpFileLogLevelClass.Information),
            queue.Enqueue(LogLevel.Critical, "e2"));
        Assert.Equal(
            new ErpFileLogEnqueueResult(true, ErpFileLogLevelClass.Information),
            queue.Enqueue(LogLevel.Error, "e3"));

        // Only errors are queued now: a new information, warning or error record is the one dropped.
        Assert.Equal(
            new ErpFileLogEnqueueResult(false, ErpFileLogLevelClass.Information),
            queue.Enqueue(LogLevel.Information, "i3"));
        Assert.Equal(
            new ErpFileLogEnqueueResult(false, ErpFileLogLevelClass.Warning),
            queue.Enqueue(LogLevel.Warning, "w1"));
        Assert.Equal(
            new ErpFileLogEnqueueResult(false, ErpFileLogLevelClass.Error),
            queue.Enqueue(LogLevel.Error, "e4"));

        Assert.Equal(3, queue.Count);
        Assert.Equal(new[] { "e1", "e2", "e3" }, DrainQueue(queue));
        Assert.Equal(new ErpFileLogDropCounts(3, 1, 1, 0), queue.TakeDropCounts());
        Assert.Equal(0, queue.TakeDropCounts().Total);
    }

    [Fact]
    public void Warnings_are_dropped_only_after_information_records_and_records_leave_in_arrival_order()
    {
        var queue = new ErpFileLogQueue(capacity: 3);
        queue.Enqueue(LogLevel.Warning, "w1");
        queue.Enqueue(LogLevel.Information, "i1");
        queue.Enqueue(LogLevel.Warning, "w2");

        Assert.Equal(
            new ErpFileLogEnqueueResult(true, ErpFileLogLevelClass.Information),
            queue.Enqueue(LogLevel.Error, "e1"));
        Assert.Equal(
            new ErpFileLogEnqueueResult(true, ErpFileLogLevelClass.Warning),
            queue.Enqueue(LogLevel.Error, "e2"));
        Assert.Equal(
            new ErpFileLogEnqueueResult(false, ErpFileLogLevelClass.Information),
            queue.Enqueue(LogLevel.Trace, "i2"));

        Assert.Equal(new[] { "w2", "e1", "e2" }, DrainQueue(queue));

        var counts = queue.TakeDropCounts();
        Assert.Equal(new ErpFileLogDropCounts(2, 1, 0, 0), counts);
        Assert.Equal(3, counts.QueueFull);

        queue.RestoreDropCounts(counts with { WriteFailed = 5 });
        Assert.Equal(new ErpFileLogDropCounts(2, 1, 0, 0), queue.TakeDropCounts());
    }

    [Fact]
    public void A_completed_file_log_queue_refuses_new_records_but_still_hands_out_queued_ones()
    {
        var queue = new ErpFileLogQueue(capacity: 2);
        queue.Enqueue(LogLevel.Information, "before");
        queue.Complete();

        Assert.True(queue.IsCompleted);
        Assert.Same(ErpFileLogEnqueueResult.Refused, queue.Enqueue(LogLevel.Error, "after"));
        Assert.Equal(0, queue.TakeDropCounts().Total);
        Assert.Equal(new[] { "before" }, DrainQueue(queue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ErpFileLogQueue(capacity: 0));
    }

    [Fact]
    public void Every_record_given_to_the_file_log_is_written_or_counted_in_a_drop_report_and_errors_are_kept()
    {
        const int recordCount = 400;
        const int errorEvery = 100;
        var directory = CreateTemporaryDirectory();
        try
        {
            // Fewer errors than the queue holds can never fill it with errors only, so none of them is dropped.
            var settings = new ErpFileLogSettings(
                directory,
                QueueCapacity: 8,
                RetentionDays: 180,
                RetentionPolicyRef: SyntheticPolicyRef,
                IncludeExceptionStackTrace: false);
            var expectedErrors = new List<string>();
            using (var writer = new ErpFileLogWriter(settings, new FixedTimeProvider(FixedNow)))
            {
                for (var index = 0; index < recordCount; index++)
                {
                    var isError = index % errorEvery == 0;
                    var line = "{\"n\":" + index + (isError ? ",\"e\":true}" : "}");
                    if (isError)
                    {
                        expectedErrors.Add(line);
                    }

                    writer.Enqueue(isError ? LogLevel.Error : LogLevel.Information, line);
                }
            }

            var lines = File.ReadAllLines(Path.Combine(directory, ErpFileLogWriter.FileNameFor(new DateOnly(2026, 11, 14))));
            var written = new List<string>();
            long dropped = 0;
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("eventName", out var eventName)
                    && eventName.GetString() == ErpFileLogWriter.DropReportEventName)
                {
                    Assert.Equal("Warning", root.GetProperty("level").GetString());
                    Assert.Equal(ErpFileLogWriter.DropReportTemplate, root.GetProperty("template").GetString());
                    var properties = root.GetProperty("properties");
                    Assert.Equal(0, properties.GetProperty(ErpFileLogWriter.DroppedErrorProperty).GetInt64());
                    Assert.Equal(0, properties.GetProperty(ErpFileLogWriter.WriteFailuresProperty).GetInt64());
                    dropped += properties.GetProperty(ErpFileLogWriter.DroppedInformationProperty).GetInt64()
                        + properties.GetProperty(ErpFileLogWriter.DroppedWarningProperty).GetInt64();
                }
                else
                {
                    written.Add(line);
                }
            }

            Assert.Equal(recordCount, written.Count + dropped);
            Assert.Equal(
                expectedErrors.ToArray(),
                written.Where(line => line.Contains("\"e\"", StringComparison.Ordinal)).ToArray());
            Assert.Equal(written.Count, written.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void The_drop_report_is_a_redacted_record_with_the_dropped_counts_by_level()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            string line;
            using (var provider = CreateProvider(directory, includeStackTrace: false))
            {
                line = provider.FormatDropReport(new ErpFileLogDropCounts(3, 2, 1, 4));
            }

            using var document = JsonDocument.Parse(line);
            var record = document.RootElement;
            Assert.Equal("Warning", record.GetProperty("level").GetString());
            Assert.Equal(ErpFileLogWriter.DropReportCategory, record.GetProperty("category").GetString());
            Assert.Equal(ErpFileLogWriter.DropReportEventName, record.GetProperty("eventName").GetString());
            Assert.Equal(ErpFileLogWriter.DropReportTemplate, record.GetProperty("template").GetString());
            var properties = record.GetProperty("properties");
            Assert.Equal(3, properties.GetProperty(ErpFileLogWriter.DroppedInformationProperty).GetInt64());
            Assert.Equal(2, properties.GetProperty(ErpFileLogWriter.DroppedWarningProperty).GetInt64());
            Assert.Equal(1, properties.GetProperty(ErpFileLogWriter.DroppedErrorProperty).GetInt64());
            Assert.Equal(4, properties.GetProperty(ErpFileLogWriter.WriteFailuresProperty).GetInt64());
            Assert.False(record.TryGetProperty("correlationId", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void The_trace_sampler_refuses_a_ratio_outside_zero_and_one(double ratio)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ErpFailureAwareSampler(ratio));
    }

    [Fact]
    public void A_trace_outside_the_sampled_share_is_recorded_but_not_marked_for_export()
    {
        var parameters = new SamplingParameters(
            default,
            ActivityTraceId.CreateRandom(),
            "SENTETIK.Operation",
            ActivityKind.Server);

        Assert.Equal(
            SamplingDecision.RecordOnly,
            new ErpFailureAwareSampler(0d).ShouldSample(parameters).Decision);
        Assert.Equal(
            SamplingDecision.RecordAndSample,
            new ErpFailureAwareSampler(1d).ShouldSample(parameters).Decision);
    }

    [Theory]
    [InlineData("status-error")]
    [InlineData("error-type")]
    [InlineData("status-code-503")]
    [InlineData("status-code-text-500")]
    public void A_failed_span_outside_the_sampled_share_is_marked_for_export(string failure)
    {
        using var activity = new Activity("SENTETIK.Operation");
        activity.IsAllDataRequested = true;
        activity.ActivityTraceFlags = ActivityTraceFlags.None;
        switch (failure)
        {
            case "status-error":
                activity.SetStatus(ActivityStatusCode.Error);
                break;
            case "error-type":
                activity.SetTag(ErpActivityRedactionProcessor.ErrorTypeTag, typeof(TimeoutException).FullName);
                break;
            case "status-code-503":
                activity.SetTag(ErpActivityRedactionProcessor.HttpResponseStatusCodeTag, 503);
                break;
            default:
                activity.SetTag(ErpActivityRedactionProcessor.HttpResponseStatusCodeTag, "500");
                break;
        }

        using var processor = new ErpFailedTraceProcessor();
        processor.OnEnd(activity);

        Assert.True(activity.Recorded);
        Assert.Equal(ErpFailedTraceProcessor.SamplingFailure, activity.GetTagItem(ErpFailedTraceProcessor.SamplingTag));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(409)]
    public void A_successful_or_client_error_span_outside_the_sampled_share_stays_unexported(int statusCode)
    {
        using var activity = new Activity("SENTETIK.Operation");
        activity.IsAllDataRequested = true;
        activity.SetTag(ErpActivityRedactionProcessor.HttpResponseStatusCodeTag, statusCode);

        using var processor = new ErpFailedTraceProcessor();
        processor.OnEnd(activity);

        Assert.False(activity.Recorded);
        Assert.Null(activity.GetTagItem(ErpFailedTraceProcessor.SamplingTag));
    }

    [Fact]
    public void A_sampled_span_is_tagged_and_a_span_dropped_by_redaction_is_never_marked_again()
    {
        using var processor = new ErpFailedTraceProcessor();

        using var sampled = new Activity("SENTETIK.Operation");
        sampled.IsAllDataRequested = true;
        sampled.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        processor.OnEnd(sampled);
        Assert.True(sampled.Recorded);
        Assert.Equal(ErpFailedTraceProcessor.SamplingRatio, sampled.GetTagItem(ErpFailedTraceProcessor.SamplingTag));

        using var dropped = new Activity("SENTETIK.Operation");
        dropped.SetStatus(ActivityStatusCode.Error);
        dropped.IsAllDataRequested = false;
        processor.OnEnd(dropped);
        Assert.False(dropped.Recorded);
        Assert.Null(dropped.GetTagItem(ErpFailedTraceProcessor.SamplingTag));
    }

    [Fact]
    public void With_no_sampled_share_only_the_failed_request_reaches_the_exporter()
    {
        var sourceName = "SENTETIK.Tracing." + Guid.NewGuid().ToString("N");
        var exported = new List<Activity>();
        using var source = new ActivitySource(sourceName);
        using (Sdk.CreateTracerProviderBuilder()
                   .AddSource(sourceName)
                   .SetSampler(new ErpFailureAwareSampler(0d))
                   .AddProcessor(new ErpActivityRedactionProcessor())
                   .AddProcessor(new ErpFailedTraceProcessor())
                   .AddProcessor(new SimpleActivityExportProcessor(new CollectingActivityExporter(exported)))
                   .Build())
        {
            using (var succeeded = source.StartActivity("SENTETIK.Succeeded", ActivityKind.Server))
            {
                Assert.NotNull(succeeded);
                succeeded.SetTag(ErpActivityRedactionProcessor.HttpResponseStatusCodeTag, 200);
            }

            using (var failed = source.StartActivity("SENTETIK.Failed", ActivityKind.Server))
            {
                Assert.NotNull(failed);
                failed.SetTag(ErpActivityRedactionProcessor.HttpResponseStatusCodeTag, 500);
            }
        }

        var span = Assert.Single(exported);
        Assert.Equal(ErpFailedTraceProcessor.SamplingFailure, span.GetTagItem(ErpFailedTraceProcessor.SamplingTag));
        Assert.Equal(500, span.GetTagItem(ErpActivityRedactionProcessor.HttpResponseStatusCodeTag));
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
                ["Observability:FileLog:RetentionDays"] = "180",
                ["Observability:FileLog:RetentionPolicyRef"] = SyntheticPolicyRef,
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

    private static string[] DrainQueue(ErpFileLogQueue queue)
    {
        var lines = new List<string>();
        while (queue.TryDequeue(out var line))
        {
            lines.Add(line);
        }

        return lines.ToArray();
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

    private sealed class SentetikInvalidOperationException : InvalidOperationException;

    private sealed class SentetikException : Exception;

    private sealed class CollectingActivityExporter(List<Activity> exported) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                exported.Add(activity);
            }

            return ExportResult.Success;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
