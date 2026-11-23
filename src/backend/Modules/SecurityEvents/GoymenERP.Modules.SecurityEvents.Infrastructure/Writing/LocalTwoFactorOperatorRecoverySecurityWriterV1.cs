using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Domain.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;

internal sealed class LocalTwoFactorOperatorRecoverySecurityWriterV1(IServiceScopeFactory scopes,
    SecurityEventRetentionResolver retention, TimeProvider clock) : ILocalTwoFactorOperatorRecoverySecurityWriterV1
{
    public async Task AppendAsync(IDbTransaction transaction, LocalTwoFactorOperatorRecoverySecurityV1 record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (transaction is not DbTransaction { Connection.State: ConnectionState.Open, IsolationLevel: IsolationLevel.Serializable } sql)
            throw new InvalidOperationException("Operator recovery requires the command's open Serializable transaction.");
        var envelope = record.ToEvent();
        if (!SecurityEventV1Validator.IsValid(envelope)) throw new ArgumentException("Invalid typed operator recovery security signal.");
        var now = clock.GetUtcNow().ToUniversalTime();
        var (until, policy) = retention.Resolve(envelope.EventType, envelope.OccurredAtUtc > now ? envelope.OccurredAtUtc : now);
        var row = SecurityEventRecord.Create(envelope, until, policy);
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetRequiredService<ICompanyContext>().IsResolved)
            throw new InvalidOperationException("Platform recovery does not acquire company authority.");
        var owner = scope.ServiceProvider.GetRequiredService<SecurityEventsDbContext>();
        if (owner.ChangeTracker.Entries().Any() || owner.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A fresh SecurityEvents owner context is required.");
        owner.Database.SetDbConnection(sql.Connection!, contextOwnsConnection: false);
        await owner.Database.UseTransactionAsync(sql, cancellationToken);
        await owner.AppendAsync(row, cancellationToken);
    }
}
