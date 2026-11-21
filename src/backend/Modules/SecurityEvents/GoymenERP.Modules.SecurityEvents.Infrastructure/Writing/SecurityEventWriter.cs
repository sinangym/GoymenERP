using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Domain.Events;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;

/// <summary>
/// Standalone <see cref="ISecurityEventWriterV1"/>. It never throws for a write failure: the (type, phase) failure
/// mode decides whether the caller may continue, and every failure increments the loss metric.
/// </summary>
/// <remarks>
/// Retention is measured from the server clock, never only from the caller's <c>OccurredAtUtc</c>: a back-dated
/// envelope cannot shorten how long its row is kept. A future-dated envelope keeps its own time as the base, so
/// <c>RetainUntilUtc</c> always ends after the event and no clock-skew tolerance is needed.
/// </remarks>
internal sealed class SecurityEventWriter(
    ISecurityEventStore store,
    SecurityEventRetentionResolver retention,
    TimeProvider timeProvider)
    : ISecurityEventWriterV1
{
    public async Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
    {
        var failureMode = SecurityEventWriteFailureModesV1.Resolve(securityEvent?.EventType, securityEvent?.Phase);
        if (securityEvent is null || !SecurityEventV1Validator.IsValid(securityEvent))
        {
            return Fail(securityEvent?.EventType, failureMode, SecurityEventWriteFailureKindsV1.InvalidEvent);
        }

        DateTimeOffset retainUntilUtc;
        string retentionPolicyRef;
        try
        {
            var serverNowUtc = timeProvider.GetUtcNow().ToUniversalTime();
            var retentionBaseUtc = securityEvent.OccurredAtUtc > serverNowUtc ? securityEvent.OccurredAtUtc : serverNowUtc;
            (retainUntilUtc, retentionPolicyRef) = retention.Resolve(securityEvent.EventType, retentionBaseUtc);
        }
        catch (InvalidOperationException)
        {
            return Fail(securityEvent.EventType, failureMode, SecurityEventWriteFailureKindsV1.RetentionUnresolved);
        }

        SecurityEventRecord record;
        try
        {
            record = SecurityEventRecord.Create(securityEvent, retainUntilUtc, retentionPolicyRef);
        }
        catch (ArgumentOutOfRangeException)
        {
            // The resolved retention does not end after the event: a retention configuration fault, not the caller's.
            return Fail(securityEvent.EventType, failureMode, SecurityEventWriteFailureKindsV1.RetentionUnresolved);
        }
        catch (ArgumentException)
        {
            return Fail(securityEvent.EventType, failureMode, SecurityEventWriteFailureKindsV1.InvalidEvent);
        }

        SecurityEventAppendOutcome outcome;
        try
        {
            outcome = await store.AppendAsync(record, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Any store failure is mapped through the failure mode; it must not escape to the caller.
        catch (Exception)
#pragma warning restore CA1031
        {
            return Fail(securityEvent.EventType, failureMode, SecurityEventWriteFailureKindsV1.StoreUnavailable);
        }

        return outcome == SecurityEventAppendOutcome.DuplicateEventId
            ? Fail(securityEvent.EventType, failureMode, SecurityEventWriteFailureKindsV1.DuplicateEventId)
            : SecurityEventWriteResultV1.Written(failureMode);
    }

    private static SecurityEventWriteResultV1 Fail(string? eventType, SecurityEventWriteFailureMode failureMode, string failureKind)
    {
        SecurityEventWriteMetrics.RecordWriteFailure(eventType, failureMode, failureKind);
        return SecurityEventWriteResultV1.NotWritten(failureMode, failureKind);
    }
}
