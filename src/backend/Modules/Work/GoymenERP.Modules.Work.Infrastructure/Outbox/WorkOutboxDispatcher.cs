using System.Diagnostics.Metrics;
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

public sealed record WorkOutboxDispatchSummary(int Delivered, int Retrying, int Poisoned, int Duplicates, int Companies);

/// <summary>
/// ERP-000F core (B-24). Reads versioned messages from registered module sources, delivers them at-least-once to
/// registered consumers with an inbox/dedup record, retries with backoff and records poison. State lives only in
/// Work-owned tables (cursor + delivery); source outboxes and consumer tables are never written here. Per company the
/// order of a source is kept: a retrying delivery holds the cursor, a poisoned one releases it.
/// </summary>
public sealed class WorkOutboxDispatcher
{
    internal const string MeterName = "GoymenERP.Work.OutboxDispatcher";
    internal const string ConsumerExceptionCode = "consumer_exception";
    internal const string PayloadMismatchCode = "payload_mismatch";
    internal const int MaxPositionLength = 256;
    private static readonly Meter Meter = new Meter(MeterName);
    private static readonly Counter<long> DeliveredCounter = Meter.CreateCounter<long>("work_outbox_delivered");
    private static readonly Counter<long> RetryCounter = Meter.CreateCounter<long>("work_outbox_retry");
    private static readonly Counter<long> PoisonCounter = Meter.CreateCounter<long>("work_outbox_poison");

    private readonly IServiceScopeFactory scopes;
    private readonly WorkOutboxDispatcherSettings settings;
    private readonly TimeProvider time;
    private readonly ILogger<WorkOutboxDispatcher> logger;

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
            foreach (var source in scope.ServiceProvider.GetServices<IOutboxSourceV1>())
            {
                try
                {
                    await DispatchSourceAsync(db, source, consumers, companyId, total, cancellationToken);
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
                    db.ChangeTracker.Clear();
                }
            }
        }

        return new WorkOutboxDispatchSummary(total.Delivered, total.Retrying, total.Poisoned, total.Duplicates, walked);
    }

    private async Task DispatchSourceAsync(
        WorkDbContext db,
        IOutboxSourceV1 source,
        IOutboxConsumerV1[] consumers,
        Guid companyId,
        Tally tally,
        CancellationToken cancellationToken)
    {
        var sourceName = source.SourceName;
        var cursor = await db.OutboxSourceCursors.SingleOrDefaultAsync(x => x.SourceName == sourceName, cancellationToken);
        var messages = await source.ReadAfterAsync(cursor?.Position, settings.BatchSize, cancellationToken);
        if (messages.Count > settings.BatchSize)
        {
            throw new InvalidOperationException("Outbox source returned more messages than requested.");
        }

        string? resolvedPosition = null;
        foreach (var message in messages)
        {
            ValidateEnvelope(message, sourceName, companyId);
            var held = false;
            foreach (var consumer in consumers.Where(c => c.Subscriptions.Any(s =>
                         s.SourceName == sourceName && s.EventType == message.EventType && s.SchemaVersion == message.SchemaVersion)))
            {
                if (!await DeliverAsync(db, consumer, message, tally, cancellationToken))
                {
                    held = true;
                }
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

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Another dispatcher instance moved the same cursor: its deliveries are deduplicated, ours repeat nothing.
            logger.LogWarning(exception, "Outbox cursor for {Source} moved concurrently; reread next cycle.", sourceName);
            db.ChangeTracker.Clear();
        }
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
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(message.PayloadJson)));
        var row = await db.OutboxDeliveries.SingleOrDefaultAsync(
            x => x.SourceName == sourceName && x.ConsumerName == consumerName && x.MessageId == messageId,
            cancellationToken);
        var now = time.GetUtcNow();

        if (row is not null)
        {
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
                    await SaveRowAsync(db, cancellationToken);
                    tally.Poisoned++;
                    PoisonCounter.Add(1);
                }

                return true;
            }

            if (row.Status != (int)WorkOutboxDeliveryStatus.Failing)
            {
                tally.Duplicates++;
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
        try
        {
            await consumer.HandleAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OutboxPermanentFailureException failure)
        {
            errorCode = failure.Code;
            permanent = true;
            logger.LogError(failure, "Outbox consumer {Consumer} refused message {MessageId} permanently ({Code}).",
                consumerName, messageId, failure.Code);
        }
        catch (Exception failure)
        {
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
        if (errorCode is null)
        {
            row.Status = (int)WorkOutboxDeliveryStatus.Delivered;
            row.DeliveredAtUtc = now;
            row.NextAttemptAtUtc = null;
            row.LastErrorCode = null;
            tally.Delivered++;
            DeliveredCounter.Add(1);
        }
        else if (permanent || attempt >= settings.MaxAttempts)
        {
            row.Status = (int)WorkOutboxDeliveryStatus.Poisoned;
            row.NextAttemptAtUtc = null;
            row.LastErrorCode = errorCode;
            tally.Poisoned++;
            PoisonCounter.Add(1);
        }
        else
        {
            row.Status = (int)WorkOutboxDeliveryStatus.Failing;
            row.NextAttemptAtUtc = now + settings.BackoffAfter(attempt);
            row.LastErrorCode = errorCode;
            tally.Retrying++;
            RetryCounter.Add(1);
            resolved = false;
        }

        if (insert)
        {
            db.OutboxDeliveries.Add(row);
        }

        return await SaveRowAsync(db, cancellationToken) && resolved;
    }

    private async Task<bool> SaveRowAsync(WorkDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
        {
            // A concurrent dispatcher recorded the same delivery first; hold the cursor and reread next cycle.
            logger.LogWarning(exception, "Outbox delivery record conflicted; reread next cycle.");
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private static void ValidateEnvelope(OutboxMessageEnvelopeV1 message, string sourceName, Guid companyId)
    {
        if (!string.Equals(message.SourceName, sourceName, StringComparison.Ordinal)
            || message.CompanyId != companyId
            || message.MessageId == Guid.Empty
            || string.IsNullOrWhiteSpace(message.EventType)
            || message.SchemaVersion < 1
            || string.IsNullOrEmpty(message.PayloadJson)
            || string.IsNullOrEmpty(message.Position)
            || message.Position.Length > MaxPositionLength
            || message.Position.Any(c => c is < '!' or > '~'))
        {
            throw new InvalidOperationException("Outbox source returned an envelope that does not match its source or the active company.");
        }
    }

    private sealed class Tally
    {
        public int Delivered;
        public int Retrying;
        public int Poisoned;
        public int Duplicates;
    }
}
