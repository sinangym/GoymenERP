using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Retention;

/// <summary>Background loop around <see cref="IdempotencyRetentionPurgeJob"/>; registered only when the purge is enabled.</summary>
internal sealed class IdempotencyRetentionPurgeHostedService(
    IdempotencyRetentionPurgeJob job,
    TimeProvider time,
    ILogger<IdempotencyRetentionPurgeHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await job.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                IdempotencyRetentionPurgeMetrics.RecordFailedCycle();
                logger.LogError(exception, "Idempotency retention purge cycle failed; it will be retried.");
            }

            try
            {
                await Task.Delay(job.PollInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
