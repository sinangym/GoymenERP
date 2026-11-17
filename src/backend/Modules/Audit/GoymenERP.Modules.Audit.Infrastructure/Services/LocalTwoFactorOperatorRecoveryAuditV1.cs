using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

internal sealed class LocalTwoFactorOperatorRecoveryAuditWriterV1(IServiceScopeFactory scopes) : ILocalTwoFactorOperatorRecoveryAuditV1
{
    public async Task AppendAsync(IDbTransaction transaction, LocalTwoFactorOperatorRecoveryAuditV1 record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (transaction is not DbTransaction { Connection.State: ConnectionState.Open, IsolationLevel: IsolationLevel.Serializable } sql)
            throw new InvalidOperationException("Operator recovery requires the command's open Serializable transaction.");
        static LocalTwoFactorRecoveryVersionAudit Map(LocalTwoFactorRecoveryVersionAuditV1 value) =>
            new(value.UserVersion, value.CredentialVersion, value.SecretVersion, value.TwoFactorEnabled);
        var auditEvent = AuditEvent.CreateLocalTwoFactorOperatorRecovery(new(record.AttemptId, record.TargetUserId,
            new(record.Operator.OperatingSystemUser, record.Operator.MachineName), Map(record.Confirmed), Map(record.Before), Map(record.After),
            (LocalTwoFactorOperatorRecoveryAuditOutcome)record.Outcome, record.Reason, record.OccurredAtUtc,
            record.CorrelationId, record.CausationId, record.IdempotencyKeyHash));
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetRequiredService<ICompanyContext>().IsResolved)
            throw new InvalidOperationException("Platform recovery does not acquire company authority.");
        var owner = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        if (owner.ChangeTracker.Entries().Any() || owner.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A fresh Audit owner context is required.");
        owner.Database.SetDbConnection(sql.Connection!, contextOwnsConnection: false);
        await owner.Database.UseTransactionAsync(sql, cancellationToken);
        await owner.AppendPlatformCredentialRecoveryAsync(auditEvent, cancellationToken);
    }
}
