using System.Text.RegularExpressions;

namespace GoymenERP.BuildingBlocks.Messaging;

/// <summary>
/// One versioned event as a module hands it to the Work outbox dispatcher (B-24, F-021, registration contract v1).
/// The module owns its outbox table; the dispatcher reads it only through <see cref="IWorkOutboxEventSourceV1"/>.
/// <see cref="EventId"/> is the stable identity of the event (the consumer dedup key is
/// <c>(CompanyId, ConsumerName, EventId)</c>), <see cref="Position"/> is an opaque cursor ordered by the source.
/// </summary>
public sealed record WorkOutboxEventV1(
    Guid EventId,
    Guid CompanyId,
    string EventType,
    int Version,
    string PayloadJson,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string Position);

/// <summary>An event type and version a source declares it publishes (no wildcard).</summary>
public sealed record WorkOutboxEventTypeV1(string EventType, int Version);

/// <summary>
/// Read-only port a module implements over its own outbox and registers with
/// <c>AddWorkOutboxEventSource&lt;TSource&gt;(sourceName, publishes)</c>. The source name comes from the registration,
/// not from the implementation, so one name maps to exactly one source. It reads only the company of the ambient
/// <c>ICompanyContext</c> (the dispatcher sets it per scope), returns at most <c>maxCount</c> events ordered by
/// position strictly after <c>afterPosition</c>, and never writes. Registering a source does not start the
/// dispatcher: it stays OFF until <c>Work:OutboxDispatcher:Enabled</c> is set (a human gate).
/// </summary>
public interface IWorkOutboxEventSourceV1
{
    Task<IReadOnlyList<WorkOutboxEventV1>> ReadAfterAsync(
        string? afterPosition,
        int maxCount,
        CancellationToken cancellationToken);
}

/// <summary>One event as delivered to a registered consumer; <see cref="SourceName"/> is the registered source name.</summary>
public sealed record WorkInboxEventV1(
    string SourceName,
    Guid EventId,
    Guid CompanyId,
    string EventType,
    int Version,
    string PayloadJson,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId);

/// <summary>A consumer's interest: one registered source, one declared event type, one version (no wildcard).</summary>
public sealed record WorkInboxSubscriptionV1(string SourceName, string EventType, int Version);

/// <summary>
/// A module's handler for versioned events, registered with
/// <c>AddWorkInboxConsumer&lt;TConsumer&gt;(consumerName, subscriptions)</c>. Delivery is at-least-once: the dispatcher
/// records one inbox row per <c>(CompanyId, ConsumerName, EventId)</c> and does not call the handler again for a
/// recorded event, even when another source hands the same event id; a crash between handling and recording can still
/// repeat a call, so the handler must itself be idempotent. It must not write another module's tables. A refusal that
/// retrying cannot fix is thrown as <see cref="OutboxPermanentFailureException"/> (poison at once); any other exception
/// is retried with the configured backoff. After a failure the handler resets its own scoped state before it throws.
/// </summary>
public interface IWorkInboxConsumerV1
{
    Task HandleAsync(WorkInboxEventV1 inboxEvent, CancellationToken cancellationToken);
}

/// <summary>Name rules of the registration contract v1, shared by registration and tests.</summary>
public static class WorkOutboxRegistrationNamesV1
{
    /// <summary>Stored column length of source and consumer names.</summary>
    public const int MaxNameLength = 128;

    /// <summary>Stored column length of event type names.</summary>
    public const int MaxEventTypeLength = 160;

    /// <summary>Lowest valid event version.</summary>
    public const int MinVersion = 1;

    // Lower-case ASCII segments joined by '.', '-' or '_' (e.g. "sales", "inventory.definite-shipment.recorded.v1").
    private static readonly Regex NamePattern = new(
        "^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*\\z", RegexOptions.CultureInvariant);

    public static bool IsValidName(string? name) =>
        name is not null && name.Length <= MaxNameLength && NamePattern.IsMatch(name);

    public static bool IsValidEventType(string? eventType) =>
        eventType is not null && eventType.Length <= MaxEventTypeLength && NamePattern.IsMatch(eventType);

    public static bool IsValidVersion(int version) => version >= MinVersion;
}
