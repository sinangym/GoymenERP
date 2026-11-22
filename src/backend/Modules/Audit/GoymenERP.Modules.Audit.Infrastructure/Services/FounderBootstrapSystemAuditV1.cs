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

internal sealed class FounderBootstrapSystemAuditV1(IServiceScopeFactory scopes) : IFounderBootstrapSystemAuditV1
{
    public async Task AppendAsync(IDbTransaction transaction, IReadOnlyList<FounderBootstrapCompanyAuditV1> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (transaction is not DbTransaction { Connection.State: ConnectionState.Open, IsolationLevel: IsolationLevel.Serializable } sql)
            throw new InvalidOperationException("Founder bootstrap requires the command's open Serializable SQL transaction.");
        if (records.Count == 0 || records.Select(row => row.CompanyId).Distinct().Count() != records.Count)
            throw new ArgumentException("A nonempty, distinct company bootstrap batch is required.");
        if (records.Select(row => (row.FounderUserId, row.Operator, row.CorrelationId, row.CausationId, row.IdempotencyKey, row.OccurredAtUtc)).Distinct().Count() != 1
            || records.Any(row => !row.CompanyIds.Order().SequenceEqual(records[0].CompanyIds.Order()))
            || !records.Select(row => row.CompanyId).Order().SequenceEqual(records[0].CompanyIds.Order()))
            throw new ArgumentException("Every row must describe the same bootstrap execution and cover each of its companies once.");
        var events = records.Select(row =>
        {
            if (string.IsNullOrWhiteSpace(row.IdempotencyKey)) throw new ArgumentException("A per-run bootstrap key is required.");
            return AuditEvent.CreateFounderBootstrap(new FounderBootstrapAuditData(Guid.NewGuid(), row.CompanyId, row.FounderUserId,
                new FounderRecoveryOperatorDeclaration(row.Operator.OperatingSystemUser, row.Operator.MachineName), row.AdminAssignmentId,
                row.CompanyIds, row.OccurredAtUtc, row.CorrelationId, row.CausationId,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.IdempotencyKey)))));
        }).ToArray();
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
