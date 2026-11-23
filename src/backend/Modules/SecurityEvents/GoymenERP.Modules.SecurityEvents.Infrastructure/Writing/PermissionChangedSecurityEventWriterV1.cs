using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Domain.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;

/// <summary>
/// The owner-transaction append of <see cref="SecurityEventTypesV1.PermissionChanged"/> (ADR-SEC-001 §4): a fresh SecurityEvents
/// context on the caller's open connection and transaction, one append, no commit. Retention is measured from the server clock as
/// in the standalone writer. Every refusal throws (fail-closed), so the caller rolls back its grant.
/// </summary>
internal sealed class PermissionChangedSecurityEventWriterV1(IServiceScopeFactory scopes,
    SecurityEventRetentionResolver retention, TimeProvider clock) : IPermissionChangedSecurityEventWriterV1
{
    public async Task AppendAsync(IDbTransaction transaction, SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        cancellationToken.ThrowIfCancellationRequested();
        if (transaction is not DbTransaction { Connection: { State: ConnectionState.Open } connection } sql)
            throw new InvalidOperationException("The permission change security event requires the owner's open transaction.");
        if (securityEvent.EventType != SecurityEventTypesV1.PermissionChanged || securityEvent.ScopeKind != SecurityEventScopeKindsV1.Company
            || securityEvent.CompanyId is not { } companyId || companyId == Guid.Empty || !SecurityEventV1Validator.IsValid(securityEvent))
            throw new ArgumentException("Only a valid company-scoped permission change security event is appended here.", nameof(securityEvent));
        var now = clock.GetUtcNow().ToUniversalTime();
        var (until, policy) = retention.Resolve(securityEvent.EventType,
            securityEvent.OccurredAtUtc > now ? securityEvent.OccurredAtUtc : now);
        var row = SecurityEventRecord.Create(securityEvent, until, policy);
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        var owner = scope.ServiceProvider.GetRequiredService<SecurityEventsDbContext>();
        if (owner.ChangeTracker.Entries().Any() || owner.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A fresh SecurityEvents owner context is required.");
        owner.Database.SetDbConnection(connection, contextOwnsConnection: false);
        await owner.Database.UseTransactionAsync(sql, cancellationToken);
        await owner.AppendAsync(row, cancellationToken);
    }
}
