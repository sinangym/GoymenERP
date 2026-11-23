using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>Own tables only. Normal reads own a read transaction; borrowed reads retain their caller's Serializable locks.</summary>
public sealed class ManufacturingSubcontractCompletionReadPortV2 : IManufacturingSubcontractCompletionReadPortV2
{
    private const int PageSize = 25;
    private readonly ManufacturingDbContext db;
    private readonly ICompanyContext company;
    private readonly ManufacturingSubcontractCompletionReaderV2 reader;

    public ManufacturingSubcontractCompletionReadPortV2(ManufacturingDbContext db, ICompanyContext company, TimeProvider time)
    {
        this.db = db; this.company = company;
        var store = new ManufacturingStore(db, company, new ReadOnlyAuditCollector(), time);
        reader = new ManufacturingSubcontractCompletionReaderV2(store, store, new ManufacturingLabelLineageReadPort(db, company), company);
    }

    public Task<ManufacturingSubcontractCompletionReadV2> GetAsync(Guid companyId, Guid orderId, CancellationToken token = default) =>
        ConsistentAsync(companyId, () => reader.GetAsync(companyId, orderId, token),
            () => new ManufacturingSubcontractCompletionReadV2(ManufacturingSubcontractCompletionOutcomeV1.Unavailable, companyId, orderId, null), token);

    public Task<ManufacturingSubcontractCompletionPageV2> ReadBySalesLineAsync(Guid companyId, Guid salesOrderId,
        Guid salesOrderLineId, int pageIndex, CancellationToken token = default)
    {
        ManufacturingSubcontractCompletionPageV2 Closed() => new ManufacturingSubcontractCompletionPageV2(ManufacturingSubcontractCompletionOutcomeV1.Unavailable,
            companyId, salesOrderId, salesOrderLineId, pageIndex, false, []);
        if (salesOrderId == Guid.Empty || salesOrderLineId == Guid.Empty || pageIndex < 0 || pageIndex > int.MaxValue / PageSize)
            return Task.FromResult(Closed());
        return ConsistentAsync(companyId, async () =>
        {
            var ids = await (from order in db.Orders.AsNoTracking()
                join plan in db.Plans.AsNoTracking() on new { order.CompanyId, Id = order.PlanId } equals new { plan.CompanyId, plan.Id }
                where order.CompanyId == companyId && plan.SalesOrderId == salesOrderId && plan.SalesOrderLineId == salesOrderLineId
                orderby order.CreatedAtUtc, order.Id
                select order.Id).Skip(pageIndex * PageSize).Take(PageSize + 1).ToArrayAsync(token);
            var items = new List<ManufacturingSubcontractCompletionReadV2>();
            foreach (var id in ids.Take(PageSize))
            {
                var answer = await reader.GetAsync(companyId, id, token);
                if (answer.Outcome != ManufacturingSubcontractCompletionOutcomeV1.Available || answer.Value is not { } proof
                    || proof.SalesOrderId != salesOrderId || proof.SalesOrderLineId != salesOrderLineId) return Closed();
                items.Add(answer);
            }
            return new ManufacturingSubcontractCompletionPageV2(ManufacturingSubcontractCompletionOutcomeV1.Available,
                companyId, salesOrderId, salesOrderLineId, pageIndex, ids.Length > PageSize, items.AsReadOnly());
        }, Closed, token);
    }

    private async Task<T> ConsistentAsync<T>(Guid companyId, Func<Task<T>> action, Func<T> unavailable, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Scoped() || db.ChangeTracker.HasChanges()) return unavailable();
        try
        {
            if (db.Database.CurrentTransaction is { } borrowed)
            {
                if (borrowed.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable) return unavailable();
                var answer = await action();
                return Scoped() ? answer : unavailable();
            }
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var result = await action();
            if (!Scoped()) return unavailable();
            await transaction.CommitAsync(token);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch { return unavailable(); }
        bool Scoped() => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId;
    }

    private sealed class ReadOnlyAuditCollector : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent) => throw new InvalidOperationException("The completion owner read cannot mutate or stage audit.");
    }
}
