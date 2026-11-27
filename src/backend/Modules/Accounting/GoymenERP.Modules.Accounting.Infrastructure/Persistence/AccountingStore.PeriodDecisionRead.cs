using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore : IAccountingPeriodDecisionStoreV1
{
    public async Task<AccountingPeriodRecord?> ReadOpeningDecisionAsync(Guid periodId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshReadFence);
        await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested(); RequireResolvedCompany();
        var row = await dbContext.Periods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodId, cancellationToken);
        await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        return row is null ? null : new(RestorePeriod(row), row.Version);
    }
    public async Task<AccountingPeriodReopeningDecisionSourceV1?> ReadReopeningDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshReadFence);
        await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested(); var company = RequireResolvedCompany();
        var proposal = await dbContext.PeriodReopenings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proposalId, cancellationToken);
        await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        if (proposal is null) return null;
        var period = await dbContext.Periods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proposal.PeriodId, cancellationToken);
        await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        var closure = await dbContext.PeriodClosures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proposal.ClosureId && x.PeriodId == proposal.PeriodId,
            cancellationToken);
        await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        if (period is null || closure is null) throw new AccountingStoreException("accounting_period_carry_source_unavailable", "Dönem önerisinin kesin kaynak bağı bulunamadı.");
        // Match the existing Work formatter: only the latest still-closed end is needed, never a history list.
        var latestClosedEnd = await (from close in dbContext.PeriodClosures.AsNoTracking()
            join candidate in dbContext.Periods.AsNoTracking() on close.PeriodId equals candidate.Id
            where close.CompanyId == company && candidate.CompanyId == company && close.ReopeningId == null
            select (DateOnly?)candidate.EndsOn).MaxAsync(cancellationToken);
        await freshReadFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        return new(new(RestorePeriod(period), period.Version), ReopeningView(proposal), ClosureView(closure))
        { LaterPeriodClosed = latestClosedEnd is { } end && end > period.EndsOn };
    }
}
