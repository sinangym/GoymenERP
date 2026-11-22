using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;

/// <summary>
/// W1-A: the Logistics tables the Work inbox sources read, untracked. The sources write their queries once over these sets;
/// <see cref="ListAsync{T}"/> runs them. The module's own DbContext answers in production (its company query filter applies on
/// top of every source's explicit company filter); a unit test answers with in-memory rows.
/// </summary>
internal abstract class LogisticsWorkInboxRows
{
    public abstract IQueryable<LoadingInstructionRow> Instructions { get; }

    public abstract IQueryable<LoadingInstructionLineRow> Lines { get; }

    public abstract IQueryable<DispatchNoteRow> DispatchNotes { get; }

    public abstract IQueryable<LogisticsPlanExcessRow> PlanExcessExceptions { get; }

    public abstract IQueryable<WeighingDifferenceRecordRow> WeighingDifferences { get; }

    public abstract Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);
}

/// <summary>The production rows: Logistics's own DbContext, no tracking.</summary>
internal sealed class LogisticsWorkInboxDbRows(LogisticsDbContext dbContext) : LogisticsWorkInboxRows
{
    public override IQueryable<LoadingInstructionRow> Instructions => dbContext.Instructions.AsNoTracking();

    public override IQueryable<LoadingInstructionLineRow> Lines => dbContext.Lines.AsNoTracking();

    public override IQueryable<DispatchNoteRow> DispatchNotes => dbContext.DispatchNotes.AsNoTracking();

    public override IQueryable<LogisticsPlanExcessRow> PlanExcessExceptions => dbContext.PlanExcessExceptions.AsNoTracking();

    public override IQueryable<WeighingDifferenceRecordRow> WeighingDifferences => dbContext.WeighingDifferences.AsNoTracking();

    public override Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken) =>
        query.ToListAsync(cancellationToken);
}
