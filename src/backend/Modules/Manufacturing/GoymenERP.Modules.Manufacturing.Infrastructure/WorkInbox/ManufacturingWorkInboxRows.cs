using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.WorkInbox;

/// <summary>
/// W1-A: the Manufacturing tables the Work inbox source reads, untracked. The source writes its queries once over these sets;
/// <see cref="ListAsync{T}"/> runs them. The module's own DbContext answers in production (its company query filter applies on
/// top of the source's explicit company filter); a unit test answers with in-memory rows.
/// </summary>
internal abstract class ManufacturingWorkInboxRows
{
    public abstract IQueryable<PlanRow> Plans { get; }

    public abstract IQueryable<PlanRevisionRow> PlanRevisions { get; }

    public abstract IQueryable<PlanRejectionRow> PlanRejections { get; }

    public abstract IQueryable<PlanReapprovalRow> PlanReapprovals { get; }

    public abstract IQueryable<OrderRow> Orders { get; }

    public abstract Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);
}

/// <summary>The production rows: Manufacturing's own DbContext, no tracking.</summary>
internal sealed class ManufacturingWorkInboxDbRows(ManufacturingDbContext dbContext) : ManufacturingWorkInboxRows
{
    public override IQueryable<PlanRow> Plans => dbContext.Plans.AsNoTracking();

    public override IQueryable<PlanRevisionRow> PlanRevisions => dbContext.PlanRevisions.AsNoTracking();

    public override IQueryable<PlanRejectionRow> PlanRejections => dbContext.PlanRejections.AsNoTracking();

    public override IQueryable<PlanReapprovalRow> PlanReapprovals => dbContext.PlanReapprovals.AsNoTracking();

    public override IQueryable<OrderRow> Orders => dbContext.Orders.AsNoTracking();

    public override Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken) =>
        query.ToListAsync(cancellationToken);
}
