namespace GoymenERP.BuildingBlocks.Security.Events;

/// <summary>
/// The only write path into the security event channel (ADR-SEC-001 §2). Implementations never throw for a
/// store failure: they map it through <see cref="SecurityEventWriteFailureModesV1"/> and return a result that tells
/// the caller whether the guarded operation may continue.
/// </summary>
public interface ISecurityEventWriterV1
{
    Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default);
}

/// <summary>Low-cardinality failure kinds, also used as the metric tag.</summary>
public static class SecurityEventWriteFailureKindsV1
{
    public const string InvalidEvent = "invalid_event";
    public const string RetentionUnresolved = "retention_unresolved";
    public const string StoreUnavailable = "store_unavailable";

    /// <summary>
    /// The EventId is already stored and the stored row could not be confirmed as the same event (different content,
    /// or a platform-scoped row the company-filtered store cannot read back).
    /// </summary>
    public const string DuplicateEventId = "duplicate_event_id";

    public static IReadOnlyList<string> All { get; } = [InvalidEvent, RetentionUnresolved, StoreUnavailable, DuplicateEventId];
}

public sealed record SecurityEventWriteResultV1
{
    private SecurityEventWriteResultV1(bool recorded, bool callerMayContinue, SecurityEventWriteFailureMode failureMode, string? failureKind)
    {
        Recorded = recorded;
        CallerMayContinue = callerMayContinue;
        FailureMode = failureMode;
        FailureKind = failureKind;
    }

    /// <summary>The event is durably stored.</summary>
    public bool Recorded { get; }

    /// <summary>
    /// False means the caller must deny the guarded operation (fail-closed). True means the caller continues with
    /// its own decision; for denial events that decision is still the denial.
    /// </summary>
    public bool CallerMayContinue { get; }

    public SecurityEventWriteFailureMode FailureMode { get; }

    public string? FailureKind { get; }

    public static SecurityEventWriteResultV1 Written(SecurityEventWriteFailureMode failureMode) =>
        new(recorded: true, callerMayContinue: true, failureMode, failureKind: null);

    public static SecurityEventWriteResultV1 NotWritten(SecurityEventWriteFailureMode failureMode, string failureKind)
    {
        if (!SecurityEventWriteFailureKindsV1.All.Contains(failureKind, StringComparer.Ordinal))
        {
            throw new ArgumentException("Unknown security event write failure kind.", nameof(failureKind));
        }

        return new(recorded: false, callerMayContinue: failureMode == SecurityEventWriteFailureMode.FailOpenWithAlarm,
            failureMode, failureKind);
    }
}
