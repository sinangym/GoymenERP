using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore : IAccountingJournalReversalDecisionStoreV1
{
    public async Task<AccountingJournalReversalDecisionSourceV1?> ReadJournalReversalDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence); await freshFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        var company = RequireResolvedCompany();
        var row = await dbContext.JournalReversalProposals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proposalId, cancellationToken);
        await freshFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        if (row is null) return null;
        var journals = await dbContext.Journals.AsNoTracking().Where(x => x.Id == row.SourceJournalId || x.Id == row.ReversalJournalId)
            .Select(x => new { x.Id, x.CompanyId, x.Kind, x.LinkedJournalId, x.PeriodId, x.TransactionDate, x.AccountingDate,
                x.ValueDate, x.RecordedAtUtc, x.StoredBy, x.CorrelationId }).ToListAsync(cancellationToken);
        await freshFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        // Same KLN-D17 owner predicate as native approval; JournalId equals the unique voucher Id.
        var cancellationActors = await dbContext.SourceDocumentVoucherDrafts.AsNoTracking()
            .Where(v => v.Status == 2 && v.JournalId == row.SourceJournalId
                && dbContext.SourceInvoiceCancellations.Any(c => c.SourceDocumentId == v.SourceDocumentId
                    && c.InvoiceId == v.InvoiceId && c.FinalizedVersion == v.FinalizedVersion))
            .Select(v => v.SourceActorId).Take(2).ToListAsync(cancellationToken);
        await freshFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        RequireCompany(company);
        // Schema requires an approved voucher JournalId == its primary Id. Retain fail-closed ambiguity protection for malformed legacy data.
        if (cancellationActors.Count > 1) return new(RestoreReversalProposal(row), false, false, null, null);
        Guid? cancellationActor = cancellationActors.Count == 0 ? null : cancellationActors[0];
        var reversal = journals.SingleOrDefault(x => x.Id == row.ReversalJournalId);
        var bound = reversal is not null && reversal.CompanyId == company && reversal.Kind == (int)JournalKind.Reversal
            && reversal.LinkedJournalId == row.SourceJournalId && reversal.PeriodId == row.PeriodId
            && reversal.TransactionDate == row.TransactionDate && reversal.AccountingDate == row.AccountingDate
            && reversal.ValueDate == row.ValueDate && reversal.RecordedAtUtc == row.DecidedAtUtc
            && Guid.TryParse(reversal.StoredBy, out var checker) && checker == row.DecidedByUserId;
        return new(RestoreReversalProposal(row), journals.Any(x => x.Id == row.SourceJournalId && x.CompanyId == company),
            bound, bound ? reversal!.CorrelationId : null, cancellationActor);
    }
}
