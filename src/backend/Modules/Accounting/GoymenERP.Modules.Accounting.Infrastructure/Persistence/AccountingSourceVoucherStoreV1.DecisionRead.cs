using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingSourceVoucherStoreV1 : IAccountingSourceVoucherDecisionStoreV1
{
    public async Task<AccountingSourceVoucherDecisionSourceV1?> ReadSourceVoucherDecisionAsync(Guid voucherId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(freshReadFence); token.ThrowIfCancellationRequested();
        var captured = Company();
        async Task Fence()
        { token.ThrowIfCancellationRequested(); RequireCompany(captured); await freshReadFence(token); token.ThrowIfCancellationRequested(); RequireCompany(captured); }
        async Task<T> Read<T>(Func<Task<T>> read)
        { await Fence(); var result = await read(); await Fence(); return result; }
        var row = await Read(() => db.SourceDocumentVoucherDrafts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == captured && x.Id == voucherId, token));
        if (row is null) return null;
        if (row.Status is not (1 or 2 or 3)) throw Refuse("source_voucher_source_unavailable");
        var lines = await Read(() => db.SourceDocumentVoucherDraftLines.AsNoTracking()
            .Where(x => x.CompanyId == captured && x.DraftId == voucherId).OrderBy(x => x.LineNumber).ToListAsync(token));
        var cancellation = await Read(() => db.SourceInvoiceCancellations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == captured && x.SourceDocumentId == row.SourceDocumentId, token));
        var reversals = await Read(() => db.JournalReversalProposals.AsNoTracking()
            .Where(x => x.CompanyId == captured && x.SourceJournalId == row.JournalId).ToListAsync(token));
        var periodOpen = await Read(() => db.Periods.AsNoTracking().AnyAsync(x => x.CompanyId == captured
            && x.Status == 1 && x.StartsOn <= row.DocumentDate && x.EndsOn >= row.DocumentDate, token));
        var stored = View(row, lines, null);
        var current = WithCancellation(View(row, lines, cancellation?.Reason), cancellation, reversals);
        // Readiness is current presentation, never a reconstructed historical Prepared snapshot.
        if (row.Status == 1 && cancellation is null) current = current with { Status = periodOpen ? "AwaitingApproval" : "PeriodWaiting" };
        var journalBound = false; string? journalCorrelation = null;
        if (row.JournalId is { } journalId)
        {
            var journal = await Read(() => db.Journals.AsNoTracking().Where(x => x.CompanyId == captured && x.Id == journalId)
                .Select(x => new { x.SourceId, x.PostingPurpose, x.PeriodId, x.TransactionDate, x.AccountingDate, x.StoredBy, x.CorrelationId }).SingleOrDefaultAsync(token));
            // Immutable header identity only. Genuine Commands plus the atomic owner commit establish the original result.
            journalBound = journal is not null && journalId == row.Id && journal.SourceId == row.SourceDocumentId
                && journal.PostingPurpose == "invoice-source" && journal.PeriodId == row.PeriodId
                && journal.TransactionDate == row.DocumentDate && journal.AccountingDate == row.DocumentDate
                && Guid.TryParse(journal.StoredBy, out var actor) && actor == row.ApprovedBy;
            journalCorrelation = journalBound ? journal!.CorrelationId : null;
        }
        await Fence();
        return new(stored, current, row.CreatedAtUtc,
            new(row.DocumentNet, row.DocumentVat, row.DocumentWithholding, row.DocumentPayable,
                row.FunctionalNet, row.FunctionalVat, row.FunctionalWithholding, row.FunctionalPayable),
            cancellation is not null, periodOpen, journalBound, journalCorrelation);
    }
}
