using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

internal sealed class LocalCredentialSelfServiceAuditWriterV1(IServiceScopeFactory scopes) : ILocalCredentialSelfServiceAuditWriterV1
{
    public async Task AppendAsync(IDbTransaction transaction, LocalCredentialSelfServiceAuditV1 record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (transaction is not DbTransaction { Connection.State: ConnectionState.Open, IsolationLevel: IsolationLevel.Serializable } sql)
            throw new InvalidOperationException("Credential self-service requires the command's open Serializable SQL transaction.");
        if (string.IsNullOrWhiteSpace(record.IdempotencyKey)) throw new ArgumentException("A self-service command key is required.");
        var audit = AuditEvent.CreateLocalCredentialSelfService(new LocalCredentialSelfServiceAuditData(Guid.NewGuid(), record.CompanyId, record.ActorUserId, record.TargetUserId,
            (LocalCredentialSelfServiceOperation)record.Operation, record.CredentialVersionBefore, record.CredentialVersionAfter, record.Method,
            record.OccurredAtUtc, record.Reason, record.CorrelationId, record.CausationId,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.IdempotencyKey)))));
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(audit.RequireCompanyId());
        var owner = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        if (owner.ChangeTracker.Entries().Any() || owner.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A fresh Audit owner context is required.");
        owner.Database.SetDbConnection(sql.Connection!, contextOwnsConnection: false);
        await owner.Database.UseTransactionAsync(sql, cancellationToken);
        await owner.AppendCompanyBatchAsync([audit], cancellationToken);
    }
}
