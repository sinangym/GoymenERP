using System.Text.RegularExpressions;

namespace GoymenERP.BuildingBlocks.Messaging;

/// <summary>
/// One versioned outbox message as a module source hands it to the platform dispatcher (ERP-000F, B-24).
/// The source owns the table; the dispatcher only reads through <see cref="IOutboxSourceV1"/> and never writes
/// to it. <see cref="Position"/> is an opaque, source-ordered cursor (the source decides its shape).
/// </summary>
public sealed record OutboxMessageEnvelopeV1(
    string SourceName,
    Guid MessageId,
    Guid CompanyId,
    string EventType,
    int SchemaVersion,
    string PayloadJson,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string Position);

/// <summary>A consumer's interest: one source, one event type, one schema version (no wildcard).</summary>
public sealed record OutboxSubscriptionV1(string SourceName, string EventType, int SchemaVersion);

/// <summary>
/// Read-only port a module implements over its own outbox. It reads only the company of the ambient
/// <c>ICompanyContext</c> (the dispatcher sets it per scope) and returns messages ordered by position.
/// </summary>
public interface IOutboxSourceV1
{
    string SourceName { get; }

    Task<IReadOnlyList<OutboxMessageEnvelopeV1>> ReadAfterAsync(
        string? afterPosition,
        int maxCount,
        CancellationToken cancellationToken);
}

/// <summary>
/// A module's handler for versioned outbox events. Delivery is at-least-once: the dispatcher de-duplicates by
/// (company, source, consumer, message id) but a crash between handling and recording can repeat a call, so the
/// handler must itself be idempotent. It must not write another module's tables (use that module's port).
/// The same consumer instance (and any scoped DbContext it holds) serves every message of a company within a cycle, so
/// after a failure the handler must clear or reset its own context before it throws; the dispatcher cannot do that for it.
/// Several dispatcher instances may run at once: the dedup record reduces, but does not remove, repeated calls.
/// </summary>
public interface IOutboxConsumerV1
{
    string ConsumerName { get; }

    IReadOnlyCollection<OutboxSubscriptionV1> Subscriptions { get; }

    Task HandleAsync(OutboxMessageEnvelopeV1 message, CancellationToken cancellationToken);
}

/// <summary>Which companies the dispatcher walks. Owned by Organization; with no implementation it delivers nothing.</summary>
public interface IOutboxCompanyDirectoryV1
{
    Task<IReadOnlyList<Guid>> ListCompanyIdsAsync(CancellationToken cancellationToken);
}

/// <summary>A consumer refusal that retrying cannot fix: the delivery is recorded as poison at once.</summary>
public sealed class OutboxPermanentFailureException : Exception
{
    private static readonly Regex CodePattern = new("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant);

    public OutboxPermanentFailureException(string code, string message)
        : base(message)
    {
        Code = IsValidCode(code) ? code : throw new ArgumentException("Failure code must be snake_case ASCII (1-64).", nameof(code));
    }

    public string Code { get; }

    public static bool IsValidCode(string? code) => code is not null && CodePattern.IsMatch(code);
}
