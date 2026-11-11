using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Domain.Events;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;

/// <summary>
/// Standalone <see cref="ISecurityEventWriterV1"/>. It never throws for a write failure: the (type, phase) failure
/// mode decides whether the caller may continue, and every failure increments the loss metric.
/// </summary>
internal sealed class SecurityEventWriter(ISecurityEventStore store, SecurityEventRetentionResolver retention)
    : ISecurityEventWriterV1
{
    public async Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
    {
        var failureMode = SecurityEventWriteFailureModesV1.Resolve(securityEvent?.EventType, securityEvent?.Phase);
        if (securityEvent is null || !SecurityEventV1Validator.IsValid(securityEvent))
        {
            return Fail(securityEvent?.EventType, failureMode, SecurityEventWriteFailureKindsV1.InvalidEvent);
        }

        SecurityEventRecord record;
        try
        {
            var (retainUntilUtc, retentionPolicyRef) = retention.Resolve(securityEvent.EventType, securityEvent.OccurredAtUtc);
            record = SecurityEventRecord.Create(securityEvent, retainUntilUtc, retentionPolicyRef);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Fail(securityEvent.EventType, failureMode, SecurityEventWriteFailureKindsV1.RetentionUnresolved);
        }

        try
        {
            await store.AppendAsync(record, cancellationToken);
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

        return SecurityEventWriteResultV1.Written(failureMode);
    }

    private static SecurityEventWriteResultV1 Fail(string? eventType, SecurityEventWriteFailureMode failureMode, string failureKind)
    {
        SecurityEventWriteMetrics.RecordWriteFailure(eventType, failureMode, failureKind);
        return SecurityEventWriteResultV1.NotWritten(failureMode, failureKind);
    }
}
