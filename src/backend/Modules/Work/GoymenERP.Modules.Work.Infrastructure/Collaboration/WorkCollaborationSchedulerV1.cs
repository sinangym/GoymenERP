using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Work.Infrastructure.Collaboration;

/// <summary>
/// Configuration section <c>Work:CollaborationScheduler</c> (AI-D29 digest and overdue cycle). OFF unless <see cref="Enabled"/>
/// is true — turning it on is a human gate, like the outbox dispatcher. No interval is defaulted: when enabled,
/// <see cref="PollInterval"/> must be configured or start-up is refused. The digest TIMES are not configured here; they are the
/// Settings parameter AI-D29 names, read per company through <see cref="IWorkDigestScheduleReaderV1"/>.
/// </summary>
public sealed class WorkCollaborationSchedulerOptions
{
    public const string SectionName = "Work:CollaborationScheduler";

    public bool Enabled { get; set; }
    public TimeSpan? PollInterval { get; set; }

    internal TimeSpan ResolvePollInterval() => PollInterval is { } poll && poll > TimeSpan.Zero
        ? poll
        : throw new InvalidOperationException($"{SectionName} is enabled but PollInterval is not configured with a valid positive value.");
}

/// <summary>Runs one <see cref="IWorkCollaborationMaintenanceV1"/> cycle per company of the shared company directory.</summary>
public sealed class WorkCollaborationScheduler(IServiceScopeFactory scopes, ILogger<WorkCollaborationScheduler> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> companyIds;
        await using (var directoryScope = scopes.CreateAsyncScope())
        {
            var directory = directoryScope.ServiceProvider.GetService<IOutboxCompanyDirectoryV1>();
            if (directory is null)
            {
                logger.LogWarning("Collaboration scheduler has no company directory registered; nothing runs.");
                return 0;
            }

            companyIds = await directory.ListCompanyIdsAsync(cancellationToken);
        }

        var walked = 0;
        foreach (var companyId in companyIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (companyId == Guid.Empty) continue;
            walked++;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
                var cycle = await scope.ServiceProvider.GetRequiredService<IWorkCollaborationMaintenanceV1>().RunCycleAsync(cancellationToken);
                if (!cycle.ScheduleAvailable)
                    logger.LogInformation("Company {CompanyId} has no available digest schedule; digest notifications stay held.", companyId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Collaboration cycle failed for company {CompanyId}; it will be retried.", companyId);
            }
        }

        return walked;
    }
}

internal sealed class WorkCollaborationSchedulerHostedService(WorkCollaborationScheduler scheduler,
    IOptions<WorkCollaborationSchedulerOptions> options, TimeProvider time, ILogger<WorkCollaborationSchedulerHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.ResolvePollInterval();
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await scheduler.RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Collaboration scheduler cycle failed; it will be retried."); }

            try { await Task.Delay(interval, time, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
