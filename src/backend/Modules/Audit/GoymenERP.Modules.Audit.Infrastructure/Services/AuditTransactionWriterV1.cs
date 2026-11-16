using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

/// <summary>Company-fenced Audit ownership; the enclosing command owns both SQL handles.</summary>
internal sealed class AuditTransactionWriterV1(
    AuditEventCollector collector,
    ICompanyContext companyContext,
    IServiceScopeFactory scopeFactory) : IAuditTransactionWriterV1
{
    public bool HasPendingDrafts => collector.Pending.Count != 0;

    public async Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
    {
        try
        {
            if (transaction is not DbTransaction sqlTransaction
                || sqlTransaction.Connection is not { State: ConnectionState.Open } connection)
                throw new InvalidOperationException("Audit requires the command's open relational transaction.");
            if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
                throw new InvalidOperationException("A resolved company is required for an Audit batch.");
            var companyId = companyContext.CompanyId;
            var drafts = collector.Pending.ToArray();
            if (drafts.Any(draft => draft.CompanyId != companyId))
                throw new InvalidOperationException("Every audit draft must belong to the resolved company.");
            var events = drafts.Select(AuditEventFactory.Create).ToArray();
            if (events.Length == 0) return;

            // A fresh owner scope cannot reuse another operation's tracked entities or transaction.
            // The owner context borrows the handles; disposing this scope never commits or closes them.
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
            var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            if (database.ChangeTracker.Entries().Any() || database.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("A fresh Audit owner context is required.");
            database.Database.SetDbConnection(connection, contextOwnsConnection: false);
            await database.Database.UseTransactionAsync(sqlTransaction, cancellationToken);
            await database.AppendCompanyBatchAsync(events, cancellationToken);
            if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
                throw new InvalidOperationException("The command company changed during the audit write.");
        }
        finally
        {
            collector.Clear();
        }
    }

    public void DiscardPending() => collector.Clear();
}
