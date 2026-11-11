using System.Diagnostics.Metrics;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Domain.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;
using static GoymenERP.SecurityEvents.UnitTests.SecurityEventFixtures;

namespace GoymenERP.SecurityEvents.UnitTests;

public sealed class SecurityEventWriterTests
{
    [Fact]
    public async Task Successful_write_appends_one_row_with_configured_retention()
    {
        var store = new RecordingStore();
        var writer = new SecurityEventWriter(store, Retention(), ServerClock());

        var result = await writer.WriteAsync(SecurityEventFixtures.AiToolCall(SecurityEventPhasesV1.Started), CancellationToken.None);

        Assert.True(result.Recorded);
        Assert.Null(result.FailureKind);
        var row = Assert.Single(store.Rows);
        Assert.Equal(ServerNow.AddYears(1), row.RetainUntilUtc);
        Assert.Equal("TEST-No157", row.RetentionPolicyRef);
        Assert.False(row.LegalHold);
        Assert.Equal(1, row.OccurrenceCount);
        Assert.Null(row.AggregationWindowStartUtc);
        Assert.Null(row.AggregationWindowEndUtc);
    }

    [Fact]
    public async Task Fail_closed_event_with_failing_store_tells_caller_to_stop_and_alarms()
    {
        using var metrics = new FailureMetricRecorder();
        var writer = new SecurityEventWriter(new FailingStore(), Retention(), ServerClock());

        var result = await writer.WriteAsync(PermissionChange(), CancellationToken.None);

        Assert.False(result.Recorded);
        Assert.False(result.CallerMayContinue);
        Assert.Equal(SecurityEventWriteFailureMode.FailClosed, result.FailureMode);
        Assert.Equal(SecurityEventWriteFailureKindsV1.StoreUnavailable, result.FailureKind);
        Assert.Contains(metrics.Snapshot(), m => m.EventType == SecurityEventTypesV1.PermissionChanged
            && m.FailureMode == nameof(SecurityEventWriteFailureMode.FailClosed)
            && m.FailureKind == SecurityEventWriteFailureKindsV1.StoreUnavailable);
    }

    [Fact]
    public async Task Ai_tool_call_started_with_failing_store_is_fail_closed()
    {
        var writer = new SecurityEventWriter(new FailingStore(), Retention(), ServerClock());

        var result = await writer.WriteAsync(SecurityEventFixtures.AiToolCall(SecurityEventPhasesV1.Started), CancellationToken.None);

        Assert.False(result.CallerMayContinue);
    }

    [Fact]
    public async Task Ai_tool_call_completed_with_failing_store_is_fail_open_with_alarm()
    {
        using var metrics = new FailureMetricRecorder();
        var writer = new SecurityEventWriter(new FailingStore(), Retention(), ServerClock());

        var result = await writer.WriteAsync(SecurityEventFixtures.AiToolCall(SecurityEventPhasesV1.Completed), CancellationToken.None);

        Assert.False(result.Recorded);
        Assert.True(result.CallerMayContinue);
        Assert.Contains(metrics.Snapshot(), m => m.EventType == SecurityEventTypesV1.AiToolCalled
            && m.FailureMode == nameof(SecurityEventWriteFailureMode.FailOpenWithAlarm));
    }

    [Fact]
    public async Task Denial_with_failing_store_keeps_the_denial_and_alarms()
    {
        using var metrics = new FailureMetricRecorder();
        var writer = new SecurityEventWriter(new FailingStore(), Retention(), ServerClock());

        var result = await writer.WriteAsync(Denial(), CancellationToken.None);

        Assert.False(result.Recorded);
        Assert.True(result.CallerMayContinue);
        Assert.Equal(SecurityEventWriteFailureMode.FailOpenWithAlarm, result.FailureMode);
        Assert.Contains(metrics.Snapshot(), m => m.EventType == SecurityEventTypesV1.AuthorizationDenied);
    }

    [Fact]
    public async Task Invalid_fail_closed_event_is_not_written_and_caller_must_stop()
    {
        var store = new RecordingStore();
        var writer = new SecurityEventWriter(store, Retention(), ServerClock());

        var result = await writer.WriteAsync(PermissionChange() with { ActorId = "admin@example.com" }, CancellationToken.None);

        Assert.Empty(store.Rows);
        Assert.False(result.CallerMayContinue);
        Assert.Equal(SecurityEventWriteFailureKindsV1.InvalidEvent, result.FailureKind);
    }

    [Fact]
    public async Task Invalid_event_with_unknown_type_is_fail_closed_with_bounded_metric_tag()
    {
        using var metrics = new FailureMetricRecorder();
        var writer = new SecurityEventWriter(new RecordingStore(), Retention(), ServerClock());

        var result = await writer.WriteAsync(Denial() with { EventType = "attacker.controlled.value" }, CancellationToken.None);

        Assert.False(result.CallerMayContinue);
        Assert.Contains(metrics.Snapshot(), m => m.EventType == SecurityEventWriteMetrics.UnknownEventType
            && m.FailureKind == SecurityEventWriteFailureKindsV1.InvalidEvent);
        Assert.DoesNotContain(metrics.Snapshot(), m => m.EventType == "attacker.controlled.value");
    }

    [Fact]
    public async Task Cancellation_requested_by_caller_is_rethrown()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var writer = new SecurityEventWriter(new CancellingStore(), Retention(), ServerClock());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteAsync(Denial(), cancellation.Token));
    }

    [Fact]
    public async Task Back_dated_event_keeps_retention_measured_from_the_server_clock()
    {
        var store = new RecordingStore();
        var writer = new SecurityEventWriter(store, Retention(), ServerClock());

        var result = await writer.WriteAsync(Denial() with { OccurredAtUtc = ServerNow.AddYears(-5) }, CancellationToken.None);

        Assert.True(result.Recorded);
        Assert.Equal(ServerNow.AddYears(2), Assert.Single(store.Rows).RetainUntilUtc);
    }

    [Fact]
    public async Task Future_dated_event_measures_retention_from_its_own_time_so_retention_ends_after_it()
    {
        var store = new RecordingStore();
        var writer = new SecurityEventWriter(store, Retention(), ServerClock());
        var occurredAt = ServerNow.AddYears(3);

        var result = await writer.WriteAsync(Denial() with { OccurredAtUtc = occurredAt }, CancellationToken.None);

        Assert.True(result.Recorded);
        Assert.Equal(occurredAt.AddYears(2), Assert.Single(store.Rows).RetainUntilUtc);
    }

    [Fact]
    public async Task Retry_with_the_same_event_id_and_content_is_recorded_once_and_reported_written()
    {
        using var metrics = new FailureMetricRecorder();
        var store = new RecordingStore();
        var writer = new SecurityEventWriter(store, Retention(), ServerClock());
        var permissionChange = PermissionChange();

        var first = await writer.WriteAsync(permissionChange, CancellationToken.None);
        var retry = await writer.WriteAsync(permissionChange, CancellationToken.None);

        Assert.True(first.Recorded);
        Assert.True(retry.Recorded);
        Assert.True(retry.CallerMayContinue);
        Assert.Single(store.Rows);
        Assert.DoesNotContain(metrics.Snapshot(), m => m.EventType == SecurityEventTypesV1.PermissionChanged);
    }

    [Fact]
    public async Task Same_event_id_with_different_content_is_refused_as_duplicate_and_alarms()
    {
        using var metrics = new FailureMetricRecorder();
        var store = new RecordingStore();
        var writer = new SecurityEventWriter(store, Retention(), ServerClock());
        var permissionChange = PermissionChange();
        await writer.WriteAsync(permissionChange, CancellationToken.None);

        var conflicting = await writer.WriteAsync(
            permissionChange with { PermissionCode = "sales.quotes.approve" },
            CancellationToken.None);

        Assert.False(conflicting.Recorded);
        Assert.False(conflicting.CallerMayContinue);
        Assert.Equal(SecurityEventWriteFailureKindsV1.DuplicateEventId, conflicting.FailureKind);
        Assert.Equal("purchasing.requests.approve", Assert.Single(store.Rows).PermissionCode);
        Assert.Contains(metrics.Snapshot(), m => m.EventType == SecurityEventTypesV1.PermissionChanged
            && m.FailureKind == SecurityEventWriteFailureKindsV1.DuplicateEventId);
    }

    [Fact]
    public void Caller_content_comparison_ignores_server_derived_retention_but_not_envelope_fields()
    {
        var denial = Denial();
        var first = SecurityEventRecord.Create(denial, OccurredAt.AddYears(2), "TEST-No134");
        var laterRetry = SecurityEventRecord.Create(denial, OccurredAt.AddYears(3), "TEST-No134-v2");
        var otherActor = SecurityEventRecord.Create(denial with { ActorId = AdminActorId }, OccurredAt.AddYears(2), "TEST-No134");

        Assert.True(first.HasSameCallerContent(laterRetry));
        Assert.False(first.HasSameCallerContent(otherActor));
    }

    [Fact]
    public void Integrity_hash_is_stable_and_covers_aggregation_columns()
    {
        var record = SecurityEventRecord.Create(Denial(), OccurredAt.AddYears(2), "TEST-No134");
        var envelope = record.ToIntegrityEnvelope();

        Assert.Equal(SecurityEventRecord.IntegrityHashLength, record.IntegrityHash.Length);
        Assert.Equal(record.IntegrityHash, SecurityEventIntegrity.Compute(envelope));
        Assert.NotEqual(record.IntegrityHash, SecurityEventIntegrity.Compute(envelope with { OccurrenceCount = 2 }));
        Assert.NotEqual(record.IntegrityHash, SecurityEventIntegrity.Compute(envelope with { AggregationWindowStartUtc = OccurredAt }));
        Assert.NotEqual(record.IntegrityHash, SecurityEventIntegrity.Compute(envelope with { AggregationWindowEndUtc = OccurredAt }));
        Assert.NotEqual(record.IntegrityHash, SecurityEventIntegrity.Compute(envelope with { RetentionPolicyRef = "OTHER" }));
    }

    [Fact]
    public void Record_refuses_retention_that_does_not_end_after_the_event()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SecurityEventRecord.Create(Denial(), OccurredAt, "TEST-No134"));
        Assert.Throws<ArgumentException>(() => SecurityEventRecord.Create(Denial(), OccurredAt.AddYears(2), " "));
    }

    private sealed class RecordingStore : ISecurityEventStore
    {
        public List<SecurityEventRecord> Rows { get; } = [];

        public Task<SecurityEventAppendOutcome> AppendAsync(SecurityEventRecord securityEvent, CancellationToken cancellationToken)
        {
            var stored = Rows.SingleOrDefault(row => row.Id == securityEvent.Id);
            if (stored is null)
            {
                Rows.Add(securityEvent);
                return Task.FromResult(SecurityEventAppendOutcome.Appended);
            }

            return Task.FromResult(stored.HasSameCallerContent(securityEvent)
                ? SecurityEventAppendOutcome.AlreadyRecorded
                : SecurityEventAppendOutcome.DuplicateEventId);
        }
    }

    private sealed class FailingStore : ISecurityEventStore
    {
        public Task<SecurityEventAppendOutcome> AppendAsync(SecurityEventRecord securityEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic store outage.");
    }

    private sealed class CancellingStore : ISecurityEventStore
    {
        public Task<SecurityEventAppendOutcome> AppendAsync(SecurityEventRecord securityEvent, CancellationToken cancellationToken) =>
            Task.FromCanceled<SecurityEventAppendOutcome>(cancellationToken);
    }

    private sealed record FailureMeasurement(string? EventType, string? FailureMode, string? FailureKind);

    private sealed class FailureMetricRecorder : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly List<FailureMeasurement> measurements = [];
        private readonly Lock gate = new();

        public FailureMetricRecorder()
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == SecurityEventWriteMetrics.MeterName
                    && instrument.Name == SecurityEventWriteMetrics.WriteFailureCounterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                string? eventType = null, failureMode = null, failureKind = null;
                foreach (var tag in tags)
                {
                    switch (tag.Key)
                    {
                        case SecurityEventWriteMetrics.EventTypeTag: eventType = tag.Value as string; break;
                        case SecurityEventWriteMetrics.FailureModeTag: failureMode = tag.Value as string; break;
                        case SecurityEventWriteMetrics.FailureKindTag: failureKind = tag.Value as string; break;
                    }
                }

                lock (gate)
                {
                    measurements.Add(new FailureMeasurement(eventType, failureMode, failureKind));
                }
            });
            listener.Start();
        }

        public IReadOnlyList<FailureMeasurement> Snapshot()
        {
            lock (gate)
            {
                return [.. measurements];
            }
        }

        public void Dispose() => listener.Dispose();
    }
}
