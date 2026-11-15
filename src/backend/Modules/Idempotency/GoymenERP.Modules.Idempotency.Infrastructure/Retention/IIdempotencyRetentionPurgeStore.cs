using GoymenERP.Modules.Idempotency.Domain.Retention;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Retention;

/// <summary>The owner-side disposal surface of idempotency records past retention (OPEN-021 / NS-12).</summary>
public interface IIdempotencyRetentionPurgeStore
{
    /// <summary>The companies that own at least one record completed strictly before <paramref name="cutoffUtc"/>.</summary>
    Task<IReadOnlyList<Guid>> ListCompaniesWithExpiredRecordsAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes at most <paramref name="batchSize"/> records of one company completed strictly before the cutoff and, in
    /// the same transaction, writes the evidence row built from <paramref name="evidence"/> with the real count.
    /// Returns the number of deleted records (zero writes no evidence).
    /// </summary>
    Task<int> PurgeBatchAsync(
        IdempotencyRetentionPurgeBatch evidence,
        int batchSize,
        CancellationToken cancellationToken);
}

/// <summary>What one purge batch is about; the deleted count is decided by the database.</summary>
public sealed record IdempotencyRetentionPurgeBatch(
    Guid RunId,
    Guid CompanyId,
    DateTimeOffset CutoffUtc,
    IdempotencyRetentionPeriod Period,
    string PolicyRef,
    DateTimeOffset RecordedAtUtc);
