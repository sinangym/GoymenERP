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

internal sealed class FounderRecoverySystemAuditV1(IServiceScopeFactory scopes) : IFounderRecoverySystemAuditV1
{
    public async Task AppendAsync(IDbTransaction transaction, IReadOnlyList<FounderRecoveryCompanyAuditV1> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (transaction is not DbTransaction { Connection.State: ConnectionState.Open, IsolationLevel: IsolationLevel.Serializable } sql)
            throw new InvalidOperationException("Founder recovery requires the command's open Serializable SQL transaction.");
        if (records.Count == 0 || records.Select(row => row.CompanyId).Distinct().Count() != records.Count)
            throw new ArgumentException("A nonempty, distinct company recovery batch is required.");
        var events = records.Select(row =>
        {
            if (string.IsNullOrWhiteSpace(row.IdempotencyKey)) throw new ArgumentException("A per-run recovery key is required.");
            return AuditEvent.CreateFounderRecovery(new FounderRecoveryAuditData(Guid.NewGuid(), row.CompanyId, row.FounderUserId,
                (FounderRecoveryAuditOperation)row.Operation, new FounderRecoveryOperatorDeclaration(row.Operator.OperatingSystemUser, row.Operator.MachineName),
                row.PreconditionCompanyIds, row.RestoredAssignmentIds, row.UserVersionBefore, row.UserVersionAfter, row.OccurredAtUtc,
                row.CorrelationId, row.CausationId, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.IdempotencyKey)))));
        }).ToArray();
        if (records.Select(row => (row.FounderUserId, row.Operation, row.Operator, row.CorrelationId, row.CausationId, row.IdempotencyKey, row.OccurredAtUtc, row.UserVersionBefore, row.UserVersionAfter)).Distinct().Count() != 1)
            throw new ArgumentException("Every row must describe the same confirmed recovery execution.");
        foreach (var auditEvent in events.OrderBy(row => row.CompanyId))
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(auditEvent.RequireCompanyId());
            var owner = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            if (owner.ChangeTracker.Entries().Any() || owner.Database.CurrentTransaction is not null) throw new InvalidOperationException("A fresh Audit owner context is required.");
            owner.Database.SetDbConnection(sql.Connection!, contextOwnsConnection: false);
            await owner.Database.UseTransactionAsync(sql, cancellationToken);
            await owner.AppendCompanyBatchAsync([auditEvent], cancellationToken);
        }
    }
}
