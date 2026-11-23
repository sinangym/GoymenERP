using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Retention;

/// <summary>
/// One retention purge cycle (OPEN-021 / NS-12): computes the cutoff from the configured period and the clock, then
/// drains each company's expired idempotency records in batches. Every batch is its own transaction with its own
/// evidence row, so a failure keeps everything already purged consistent and the next cycle continues.
/// </summary>
public sealed class IdempotencyRetentionPurgeJob(
    IIdempotencyRetentionPurgeStore store,
    IdempotencyRetentionPurgeSettings settings,
    TimeProvider time,
    ILogger<IdempotencyRetentionPurgeJob> logger)
{
    public TimeSpan PollInterval => settings.PollInterval;

    public async Task<IdempotencyRetentionPurgeSummary> RunOnceAsync(CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var runId = Guid.NewGuid();
        var now = time.GetUtcNow();
        var cutoff = settings.Period.CutoffAt(now);
        var companies = await store.ListCompaniesWithExpiredRecordsAsync(cutoff, cancellationToken);
        var purgedCompanies = 0;
        var purged = 0L;
        foreach (var companyId in companies)
        {
            var companyPurged = 0L;
            int deleted;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                deleted = await store.PurgeBatchAsync(
                    new IdempotencyRetentionPurgeBatch(
                        runId, companyId, cutoff, settings.Period, settings.PolicyRef, time.GetUtcNow()),
                    settings.BatchSize,
                    cancellationToken);
                if (deleted < 0 || deleted > settings.BatchSize)
                {
                    throw new InvalidOperationException("Saklama imha deposu beklenmeyen bir silinen kayıt sayısı döndürdü.");
                }

                if (deleted > 0)
                {
                    IdempotencyRetentionPurgeMetrics.RecordPurged(deleted);
                    companyPurged += deleted;
                }
            }
            while (deleted == settings.BatchSize);

            if (companyPurged > 0)
            {
                purgedCompanies++;
                purged += companyPurged;
            }
        }

        var elapsed = time.GetElapsedTime(started);
        IdempotencyRetentionPurgeMetrics.RecordCycleDuration(elapsed);
        if (purged > 0)
        {
            logger.LogInformation(
                "Idempotency retention purge {RunId} disposed of {Purged} records of {Companies} companies older than {CutoffUtc} (policy {PolicyRef}).",
                runId, purged, purgedCompanies, cutoff, settings.PolicyRef);
        }

        return new IdempotencyRetentionPurgeSummary(runId, cutoff, purgedCompanies, purged);
    }
}

/// <summary>The outcome of one purge cycle.</summary>
public sealed record IdempotencyRetentionPurgeSummary(
    Guid RunId,
    DateTimeOffset CutoffUtc,
    int PurgedCompanies,
    long PurgedRecords);
