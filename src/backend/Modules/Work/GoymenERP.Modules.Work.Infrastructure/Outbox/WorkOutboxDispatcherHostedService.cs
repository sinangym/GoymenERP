using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Work.Infrastructure.Outbox;

/// <summary>Background loop around <see cref="WorkOutboxDispatcher"/>; only registered when the flag is on.</summary>
internal sealed class WorkOutboxDispatcherHostedService(
    WorkOutboxDispatcher dispatcher,
    TimeProvider time,
    ILogger<WorkOutboxDispatcherHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var summary = await dispatcher.RunOnceAsync(stoppingToken);
                if (summary.Delivered + summary.Retrying + summary.Poisoned > 0)
                {
                    logger.LogInformation(
                        "Outbox dispatch cycle: delivered {Delivered}, retrying {Retrying}, poisoned {Poisoned}.",
                        summary.Delivered, summary.Retrying, summary.Poisoned);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatch cycle failed; it will be retried.");
            }

            try
            {
                await Task.Delay(dispatcher.PollInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
