using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Work.Infrastructure.Outbox;

public sealed record WorkOutboxDispatchSummary(
    int Delivered,
    int Retrying,
    int Poisoned,
    int Duplicates,
    int Companies,
    int InvalidEnvelopes = 0,
    int CrossSourceDuplicates = 0,
    int Unrouted = 0);

/// <summary>
/// ERP-000F core (B-24). Reads versioned messages from registered module sources, delivers them at-least-once to
/// registered consumers with an inbox/dedup record, retries with backoff and records poison. State lives only in
/// Work-owned tables (cursor + delivery); source outboxes and consumer tables are never written here. Per company the
/// order of a source is kept: a retrying delivery holds the cursor, a poisoned one releases it. The inbox/dedup key
/// is (company, consumer, message id): a message id already recorded for a consumer is never handed to it again, even
/// when another source returns it (registration contract v1, F-021). Source and consumer names must be unique within a
/// company scope; a duplicate name fails the source without moving its cursor.
/// </summary>
public sealed class WorkOutboxDispatcher
{
    internal const string MeterName = WorkOutboxDispatcherMetrics.MeterName;
    internal const string ConsumerExceptionCode = "consumer_exception";
    internal const string PayloadMismatchCode = "payload_mismatch";
    internal const int MaxPositionLength = 256;
    internal const int MaxEventTypeLength = 160;
    internal const int MaxNameLength = 128;

    private readonly IServiceScopeFactory scopes;
    private readonly WorkOutboxDispatcherSettings settings;
    private readonly TimeProvider time;
    private readonly ILogger<WorkOutboxDispatcher> logger;

    // In-process failure backoff per (company, source): a persistent storage error must not re-run consumer handlers
    // on every poll. Reuses the configured backoff; losing it on restart only costs one extra attempt.
    private readonly ConcurrentDictionary<(Guid CompanyId, string SourceName), (int Failures, DateTimeOffset Until)> sourceFailures = new ConcurrentDictionary<(Guid CompanyId, string SourceName), (int Failures, DateTimeOffset Until)>();

    public WorkOutboxDispatcher(
        IServiceScopeFactory scopes,
        IOptions<WorkOutboxDispatcherOptions> options,
        TimeProvider time,
        ILogger<WorkOutboxDispatcher> logger)
    {
        this.scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        settings = (options ?? throw new ArgumentNullException(nameof(options))).Value.Resolve();
        this.time = time ?? throw new ArgumentNullException(nameof(time));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal TimeSpan PollInterval => settings.PollInterval;

    public async Task<WorkOutboxDispatchSummary> RunOnceAsync(CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        try
        {
            return await RunCycleAsync(cancellationToken);
        }
        finally
        {
            WorkOutboxDispatcherMetrics.RecordCycleDuration(time.GetElapsedTime(started));
        }
    }

    private async Task<WorkOutboxDispatchSummary> RunCycleAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> companyIds;
        await using (var directoryScope = scopes.CreateAsyncScope())
        {
            var directory = directoryScope.ServiceProvider.GetService<IOutboxCompanyDirectoryV1>();
            if (directory is null)
            {
                logger.LogWarning("Outbox dispatcher has no company directory registered; nothing is delivered.");
                return new WorkOutboxDispatchSummary(0, 0, 0, 0, 0);
            }

            companyIds = await directory.ListCompanyIdsAsync(cancellationToken);
        }

        var total = new Tally();
        var walked = 0;
        foreach (var companyId in companyIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (companyId == Guid.Empty)
            {
                logger.LogError("Outbox dispatcher skipped an empty company id from the directory.");
                continue;
            }

            walked++;
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
            var db = scope.ServiceProvider.GetRequiredService<WorkDbContext>();
            var consumers = scope.ServiceProvider.GetServices<IOutboxConsumerV1>().ToArray();
            var sources = scope.ServiceProvider.GetServices<IOutboxSourceV1>().ToArray();
            var duplicateSourceNames = DuplicateNames(sources.Select(s => s.SourceName));
            foreach (var source in sources)
            {
                var failureKey = (companyId, source.SourceName ?? string.Empty);
                if (sourceFailures.TryGetValue(failureKey, out var failed) && failed.Until > time.GetUtcNow())
                {
                    continue;
                }

                try
                {
                    await DispatchSourceAsync(db, source, consumers, duplicateSourceNames, companyId, total, cancellationToken);
                    sourceFailures.TryRemove(failureKey, out _);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // One bad source or company must not stop the others; the cursor was not advanced past it.
                    logger.LogError(exception, "Outbox source {Source} failed for company {CompanyId}; retried next cycle.",
                        source.SourceName, companyId);
                    WorkOutboxDispatcherMetrics.RecordSourceFailure(
                        IsStoredName(source.SourceName) ? source.SourceName : WorkOutboxDispatcherMetrics.InvalidSourceName);
                    db.ChangeTracker.Clear();
                    var failures = (sourceFailures.TryGetValue(failureKey, out var previous) ? previous.Failures : 0) + 1;
                    sourceFailures[failureKey] = (failures, time.GetUtcNow() + settings.BackoffAfter(failures));
                }
            }
        }

        return new WorkOutboxDispatchSummary(
            total.Delivered, total.Retrying, total.Poisoned, total.Duplicates, walked, total.Invalid,
            total.CrossSourceDuplicates, total.Unrouted);
    }

    private async Task DispatchSourceAsync(
        WorkDbContext db,
        IOutboxSourceV1 source,
        IOutboxConsumerV1[] consumers,
        IReadOnlySet<string> duplicateSourceNames,
        Guid companyId,
        Tally tally,
        CancellationToken cancellationToken)
    {
        var sourceName = source.SourceName;
        if (!IsStoredName(sourceName))
        {
            throw new InvalidOperationException("Outbox source name is empty, too long or not printable ASCII.");
        }

        if (duplicateSourceNames.Contains(sourceName))
        {
            // Two sources under one name would share one cursor; neither may move it.
            throw new InvalidOperationException("Outbox source name is registered more than once.");
        }

        foreach (var consumer in consumers)
        {
            if (!IsStoredName(consumer.ConsumerName))
            {
                throw new InvalidOperationException("Outbox consumer name is empty, too long or not printable ASCII.");
            }
        }

        if (DuplicateNames(consumers.Select(c => c.ConsumerName)).Count > 0)
        {
            // Two consumers under one name would share one inbox key; the second would never see a message.
            throw new InvalidOperationException("Outbox consumer name is registered more than once.");
        }

        var cursor = await db.OutboxSourceCursors.SingleOrDefaultAsync(x => x.SourceName == sourceName, cancellationToken);
        var messages = await source.ReadAfterAsync(cursor?.Position, settings.BatchSize, cancellationToken);
        if (messages.Count > settings.BatchSize)
        {
            throw new InvalidOperationException("Outbox source returned more messages than requested.");
        }

        string? resolvedPosition = null;
        foreach (var message in messages)
        {
            if (!IsValidEnvelope(message, sourceName, companyId))
            {
                // A bad envelope must not wedge the source: alarm, then release the cursor past it. Its position is
                // the only thing needed for that, so an unusable position is a source failure instead.
                if (!IsValidPosition(message.Position))
                {
                    throw new InvalidOperationException("Outbox source returned an envelope without a usable position.");
                }

                logger.LogError(
                    "Outbox source {Source} returned an invalid envelope at position {Position} for company {CompanyId}; skipped.",
                    sourceName, message.Position, companyId);
                tally.Invalid++;
                WorkOutboxDispatcherMetrics.RecordInvalidEnvelope(sourceName);
                resolvedPosition = message.Position;
                continue;
            }

            var held = false;
            var routed = false;
            foreach (var consumer in consumers.Where(c => c.Subscriptions.Any(s =>
                         s.SourceName == sourceName && s.EventType == message.EventType && s.SchemaVersion == message.SchemaVersion)))
            {
                routed = true;
                if (!await DeliverAsync(db, consumer, message, tally, cancellationToken))
                {
                    held = true;
                }
            }

            if (!routed)
            {
                // No subscriber is not an error (a later consumer starts from its own first delivery), but it is visible.
                tally.Unrouted++;
                WorkOutboxDispatcherMetrics.RecordUnrouted(sourceName);
            }

            if (held)
            {
                break;
            }

            resolvedPosition = message.Position;
        }

        if (resolvedPosition is null)
        {
            return;
        }

        if (cursor is not null && db.Entry(cursor).State == EntityState.Detached)
        {
            // A conflict recovery cleared the change tracker; the earlier deliveries' progress must still be saved.
            cursor = await db.OutboxSourceCursors.SingleOrDefaultAsync(x => x.SourceName == sourceName, cancellationToken);
        }

        var now = time.GetUtcNow();
        if (cursor is null)
        {
            db.OutboxSourceCursors.Add(new WorkOutboxSourceCursorRow
            {
                CompanyId = companyId, SourceName = sourceName, Position = resolvedPosition, UpdatedAtUtc = now,
            });
        }
        else
        {
            cursor.Position = resolvedPosition;
            cursor.UpdatedAtUtc = now;
        }

        var inserted = cursor is null;
        await SaveOrConflictAsync(
            db,
            ct => inserted ? db.OutboxSourceCursors.AsNoTracking().AnyAsync(x => x.SourceName == sourceName, ct) : Task.FromResult(false),
            cancellationToken);
    }

    /// <summary>Returns true when the message is resolved for this consumer (delivered, duplicate or poisoned).</summary>
    private async Task<bool> DeliverAsync(
        WorkDbContext db,
        IOutboxConsumerV1 consumer,
        OutboxMessageEnvelopeV1 message,
        Tally tally,
        CancellationToken cancellationToken)
    {
        var consumerName = consumer.ConsumerName;
        var messageId = message.MessageId;
        var sourceName = message.SourceName;
        var eventType = message.EventType;
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(message.PayloadJson)));
        var row = await db.OutboxDeliveries.SingleOrDefaultAsync(
            x => x.ConsumerName == consumerName && x.MessageId == messageId,
            cancellationToken);
        var now = time.GetUtcNow();

        if (row is not null)
        {
            if (!string.Equals(row.SourceName, sourceName, StringComparison.Ordinal))
            {
                // The consumer already has an inbox record of this message id from another source. The first record is
                // the evidence and stays untouched; the handler is not called again for the same event.
                logger.LogError(
                    "Outbox message {MessageId} from {Source} was already recorded for {Consumer} from another source; not redelivered.",
                    messageId, sourceName, consumerName);
                tally.CrossSourceDuplicates++;
                WorkOutboxDispatcherMetrics.RecordCrossSourceDuplicate(sourceName, consumerName, eventType);
                return true;
            }

            if (!string.Equals(row.PayloadSha256, payloadHash, StringComparison.Ordinal))
            {
                logger.LogError("Outbox message {MessageId} of {Source} changed after first read for {Consumer}.",
                    messageId, sourceName, consumerName);
                if (row.Status == (int)WorkOutboxDeliveryStatus.Failing)
                {
                    row.Status = (int)WorkOutboxDeliveryStatus.Poisoned;
                    row.AttemptCount++;
                    row.LastAttemptAtUtc = now;
                    row.NextAttemptAtUtc = null;
                    row.LastErrorCode = PayloadMismatchCode;
                    // The stored hash is the first-read evidence and stays; the guard forbids changing it.
                    if (await SaveRowAsync(db, consumerName, messageId, false, cancellationToken))
                    {
                        tally.Poisoned++;
                        WorkOutboxDispatcherMetrics.RecordPoison(sourceName, consumerName, eventType);
                    }
                }

                return true;
            }

            if (row.Status != (int)WorkOutboxDeliveryStatus.Failing)
            {
                tally.Duplicates++;
                WorkOutboxDispatcherMetrics.RecordDuplicate(sourceName, consumerName, eventType);
                return true;
            }

            if (row.NextAttemptAtUtc > now)
            {
                tally.Retrying++;
                return false;
            }
        }

        var attempt = (row?.AttemptCount ?? 0) + 1;
        string? errorCode = null;
        var permanent = false;
        var handlerStarted = time.GetTimestamp();
        try
        {
            await consumer.HandleAsync(message, cancellationToken);
            WorkOutboxDispatcherMetrics.RecordHandlerDuration(
                time.GetElapsedTime(handlerStarted), sourceName, consumerName, eventType, WorkOutboxDispatcherMetrics.OutcomeSucceeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OutboxPermanentFailureException failure)
        {
            WorkOutboxDispatcherMetrics.RecordHandlerDuration(
                time.GetElapsedTime(handlerStarted), sourceName, consumerName, eventType, WorkOutboxDispatcherMetrics.OutcomeRefused);
            errorCode = failure.Code;
            permanent = true;
            logger.LogError(failure, "Outbox consumer {Consumer} refused message {MessageId} permanently ({Code}).",
                consumerName, messageId, failure.Code);
        }
        catch (Exception failure)
        {
            WorkOutboxDispatcherMetrics.RecordHandlerDuration(
                time.GetElapsedTime(handlerStarted), sourceName, consumerName, eventType, WorkOutboxDispatcherMetrics.OutcomeFailed);
            errorCode = ConsumerExceptionCode;
            logger.LogWarning(failure, "Outbox consumer {Consumer} failed message {MessageId} on attempt {Attempt}.",
                consumerName, messageId, attempt);
        }

        var insert = row is null;
        row ??= new WorkOutboxDeliveryRow
        {
            CompanyId = message.CompanyId, SourceName = sourceName, ConsumerName = consumerName, MessageId = messageId,
            EventType = message.EventType, SchemaVersion = message.SchemaVersion, PayloadSha256 = payloadHash,
            FirstAttemptAtUtc = now,
        };
        row.AttemptCount = attempt;
        row.LastAttemptAtUtc = now;
        var resolved = true;
        Outcome outcome;
        if (errorCode is null)
        {
            row.Status = (int)WorkOutboxDeliveryStatus.Delivered;
            row.DeliveredAtUtc = now;
            row.NextAttemptAtUtc = null;
            row.LastErrorCode = null;
            outcome = Outcome.Delivered;
        }
        else if (permanent || attempt >= settings.MaxAttempts)
        {
            row.Status = (int)WorkOutboxDeliveryStatus.Poisoned;
            row.NextAttemptAtUtc = null;
            row.LastErrorCode = errorCode;
            outcome = Outcome.Poisoned;
        }
        else
        {
            row.Status = (int)WorkOutboxDeliveryStatus.Failing;
            row.NextAttemptAtUtc = now + settings.BackoffAfter(attempt);
            row.LastErrorCode = errorCode;
            outcome = Outcome.Retrying;
            resolved = false;
        }

        if (insert)
        {
            db.OutboxDeliveries.Add(row);
        }

        if (!await SaveRowAsync(db, consumerName, messageId, insert, cancellationToken))
        {
            return false;
        }

        switch (outcome)
        {
            case Outcome.Delivered:
                tally.Delivered++;
                WorkOutboxDispatcherMetrics.RecordDelivered(sourceName, consumerName, eventType);
                break;
            case Outcome.Poisoned:
                tally.Poisoned++;
                WorkOutboxDispatcherMetrics.RecordPoison(sourceName, consumerName, eventType);
                break;
            default:
                tally.Retrying++;
                WorkOutboxDispatcherMetrics.RecordRetry(sourceName, consumerName, eventType);
                break;
        }

        return resolved;
    }

    // The race check uses the inbox/dedup key (company via the query filter, consumer, message id): a row of the same
    // message written first by another dispatcher, from any source, is a conflict and not a storage error.
    private Task<bool> SaveRowAsync(
        WorkDbContext db, string consumerName, Guid messageId, bool insert, CancellationToken cancellationToken) =>
        SaveOrConflictAsync(
            db,
            ct => insert
                ? db.OutboxDeliveries.AsNoTracking().AnyAsync(
                    x => x.ConsumerName == consumerName && x.MessageId == messageId, ct)
                : Task.FromResult(false),
            cancellationToken);

    /// <summary>
    /// A save failure is a conflict only when it is a concurrency exception, or when the row this instance tried to
    /// insert is now visible (another instance won the race on the key). Anything else is a real storage error.
    /// </summary>
    private async Task<bool> SaveOrConflictAsync(
        WorkDbContext db,
        Func<CancellationToken, Task<bool>> rowWonByAnother,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception, "Outbox record changed concurrently; reread next cycle.");
            db.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException exception)
        {
            db.ChangeTracker.Clear();
            if (await rowWonByAnother(cancellationToken))
            {
                logger.LogWarning(exception, "Outbox record was written first by another dispatcher; reread next cycle.");
                return false;
            }

            throw;
        }
    }

    private static bool IsPrintableAscii(string value) => value.All(c => c is >= '!' and <= '~');

    private static HashSet<string> DuplicateNames(IEnumerable<string?> names) =>
        names.Where(n => n is not null)
            .GroupBy(n => n!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsStoredName([NotNullWhen(true)] string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= MaxNameLength && IsPrintableAscii(value);

    private static bool IsValidPosition(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= MaxPositionLength && IsPrintableAscii(value);

    private static bool IsValidEnvelope(OutboxMessageEnvelopeV1 message, string sourceName, Guid companyId) =>
        string.Equals(message.SourceName, sourceName, StringComparison.Ordinal)
        && message.CompanyId == companyId
        && message.MessageId != Guid.Empty
        && !string.IsNullOrEmpty(message.EventType)
        && message.EventType.Length <= MaxEventTypeLength
        && IsPrintableAscii(message.EventType)
        && message.SchemaVersion >= 1
        && !string.IsNullOrEmpty(message.PayloadJson)
        && IsValidPosition(message.Position);

    private enum Outcome
    {
        Delivered,
        Poisoned,
        Retrying,
    }

    private sealed class Tally
    {
        public int Delivered;
        public int Retrying;
        public int Poisoned;
        public int Duplicates;
        public int Invalid;
        public int CrossSourceDuplicates;
        public int Unrouted;
    }
}
