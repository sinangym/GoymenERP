using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Domain.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;

internal enum SecurityEventAppendOutcome
{
    Appended,

    /// <summary>The EventId was already stored with the same caller-supplied content (an idempotent retry).</summary>
    AlreadyRecorded,

    /// <summary>The EventId was already stored and the stored row is not confirmed as the same event.</summary>
    DuplicateEventId,
}

internal interface ISecurityEventStore
{
    Task<SecurityEventAppendOutcome> AppendAsync(SecurityEventRecord securityEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Standalone store: each append runs in a fresh scope with its own <see cref="SecurityEventsDbContext"/>, so it
/// uses its own connection and its own transaction, independent of the caller's unit of work. A primary key
/// violation on the EventId is read back through the company query filter (never bypassed): a company-scoped row
/// with the same caller content is an idempotent retry; anything else is reported as a duplicate EventId.
/// </summary>
internal sealed class StandaloneSecurityEventStore(IServiceScopeFactory scopeFactory) : ISecurityEventStore
{
    public async Task<SecurityEventAppendOutcome> AppendAsync(SecurityEventRecord securityEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SecurityEventsDbContext>();
            await dbContext.AppendAsync(securityEvent, cancellationToken);
            return SecurityEventAppendOutcome.Appended;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return await ReadBackDuplicateAsync(securityEvent, cancellationToken);
        }
    }

    private async Task<SecurityEventAppendOutcome> ReadBackDuplicateAsync(
        SecurityEventRecord securityEvent,
        CancellationToken cancellationToken)
    {
        if (securityEvent.ScopeKind != SecurityEventScopeKindsV1.Company || securityEvent.CompanyId is not { } companyId)
        {
            // Platform rows are invisible to the company query filter; they cannot be confirmed without a bypass.
            return SecurityEventAppendOutcome.DuplicateEventId;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<SecurityEventsDbContext>();
        var stored = await dbContext.SecurityEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == securityEvent.Id, cancellationToken);
        return stored is not null && stored.HasSameCallerContent(securityEvent)
            ? SecurityEventAppendOutcome.AlreadyRecorded
            : SecurityEventAppendOutcome.DuplicateEventId;
    }
}
