using System.Data;
using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingSourceVoucherStoreV1
{
    /// <summary>The cancelled source and its posted journal are immutable owners of the proposal. The user chooses
    /// an existing open period/date; proposal, voucher version, audit and command receipt commit together.
    /// The existing reversal approval owns maker/checker, exact reversed lines and period concurrency.</summary>
    public async Task<AccountingSourceVoucherViewV1> ProposeCancellationReversalAsync(Guid id,
        ProposeAccountingSourceVoucherReversalV1 request, Guid actorId,
        Func<CancellationToken, Task> freshAuthorityFence, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var companyId = Company();
        if (actorId == Guid.Empty || !Guid.TryParse(metadata.ActorId, out var auditActor) || actorId != auditActor)
            throw Refuse("source_voucher_approval_refused");
        await freshAuthorityFence(cancellationToken);
        var sourceId = await db.SourceDocumentVoucherDrafts.AsNoTracking().Where(x => x.Id == id)
            .Select(x => (Guid?)x.SourceDocumentId).SingleOrDefaultAsync(cancellationToken)
            ?? throw Refuse("source_voucher_not_found");
        RequireCompany(companyId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            // Keep the established cancellation -> voucher lock order used by final approval/cancellation.
            var cancellation = await db.SourceInvoiceCancellations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.SourceDocumentId == sourceId, cancellationToken);
            var row = await db.SourceDocumentVoucherDrafts.SingleAsync(x => x.Id == id, cancellationToken);
            if (request.ExpectedVersion < 1 || row.Version != request.ExpectedVersion)
                throw new AccountingVersionConflictException(request.ExpectedVersion, row.Version);
            if (row.Status != 2 || row.JournalId is not { } journalId || cancellation is null)
                throw Refuse("source_voucher_reversal_not_proposable");
            var existing = await db.JournalReversalProposals.AsNoTracking()
                .Where(x => x.SourceJournalId == journalId).ToListAsync(cancellationToken);
            var lines = await db.SourceDocumentVoucherDraftLines.AsNoTracking().Where(x => x.DraftId == id)
                .OrderBy(x => x.LineNumber).ToListAsync(cancellationToken);
            var before = WithCancellation(View(row, lines, cancellation.Reason), cancellation, existing);
            if (existing.Any(x => x.Status is (int)AccountingJournalReversalProposalStatusV1.Pending
                or (int)AccountingJournalReversalProposalStatusV1.Approved)
                || await db.Journals.AnyAsync(x => x.Kind == (int)JournalKind.Reversal && x.LinkedJournalId == journalId, cancellationToken))
                throw Refuse("source_voucher_reversal_not_proposable");
            var period = await db.Periods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PeriodId, cancellationToken);
            if (period is null || period.Status != 1 || request.AccountingDate < period.StartsOn || request.AccountingDate > period.EndsOn)
                throw Refuse("source_voucher_reversal_period_not_open");
            var proposal = AccountingJournalReversalPolicyV1.Propose(companyId, actorId,
                new(request.ProposalId, journalId, request.PeriodId, request.TransactionDate,
                    request.AccountingDate, request.ValueDate, request.Reason), clock.GetUtcNow());
            await freshAuthorityFence(cancellationToken);
            RequireCompany(companyId);
            // Reuse the actual Accounting proposal writer. It never commits the borrowed transaction.
            await posting.CreateAsync(proposal, freshAuthorityFence, metadata with { Completion = null }, cancellationToken);
            await freshAuthorityFence(cancellationToken);
            RequireCompany(companyId);
            var version = row.Version;
            row.Version = checked(version + 1);
            row.UpdatedAtUtc = clock.GetUtcNow();
            var result = before with { Version = row.Version, ReversalProposal = ProposalView(proposal) };
            audit.Enqueue(new AuditEventDraft(companyId, "accounting.source-voucher.cancellation-reversal.proposed.v1",
                "Accounting.SourceVoucher", id.ToString("D"), "ProposeCancellationReversal", version, row.Version,
                AuditActorTypes.User, metadata.ActorId, metadata.PermissionCode, metadata.RoleCodes,
                JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(result, Json),
                "Resmen iptal edilmiş faturanın yevmiyesi için seçilmiş açık dönemde ters kayıt önerildi.", null,
                AuditAiInvolvement.None, null, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
                metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
            if (metadata.Completion?.Invoke(result) is { } command)
                db.Commands.Add(new AccountingCommandRow { CompanyId = companyId, CommandName = command.CommandName,
                    IdempotencyKeyHash = command.IdempotencyKeyHash, ActorId = command.ActorId, PayloadHash = command.RequestHash,
                    ResultJson = command.ResultJson, CorrelationId = command.OriginalCorrelationId, RecordedAtUtc = clock.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw Refuse(AccountingStoreCodes.ConcurrentChange);
        }
        catch (Exception exception) when (exception is SqlException { Number: 1205 }
            || exception is DbUpdateException { InnerException: SqlException { Number: 1205 } }
            || exception is InvalidOperationException
                { InnerException: DbUpdateException { InnerException: SqlException { Number: 1205 } } })
        {
            db.ChangeTracker.Clear();
            throw Refuse(AccountingStoreCodes.ConcurrentChange);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static AccountingSourceVoucherViewV1 WithCancellation(AccountingSourceVoucherViewV1 view,
        AccountingSourceInvoiceCancellationRow? cancellation, IEnumerable<AccountingJournalReversalProposalRow> proposals)
    {
        if (cancellation is null) return view;
        if (cancellation.CompanyId != view.CompanyId || cancellation.SourceDocumentId != view.SourceDocumentId
            || cancellation.InvoiceId != view.InvoiceId || cancellation.FinalizedVersion != view.FinalizedVersion
            || cancellation.DocumentKind != view.DocumentKind)
            throw Refuse("source_voucher_source_conflict");
        var proposal = proposals.Where(x => x.CompanyId == view.CompanyId && x.SourceJournalId == view.JournalId)
            .OrderByDescending(x => x.Status == (int)AccountingJournalReversalProposalStatusV1.Approved)
            .ThenByDescending(x => x.Status == (int)AccountingJournalReversalProposalStatusV1.Pending)
            .ThenByDescending(x => x.ProposedAtUtc).ThenBy(x => x.Id).FirstOrDefault();
        var value = proposal is null ? null : new AccountingJournalReversalProposalViewV1(proposal.Id,
            proposal.CompanyId, proposal.SourceJournalId, proposal.ReversalJournalId, proposal.PeriodId,
            proposal.TransactionDate, proposal.AccountingDate, proposal.ValueDate, proposal.Reason,
            proposal.ProposedByUserId, proposal.ProposedAtUtc, ((AccountingJournalReversalProposalStatusV1)proposal.Status).ToString(),
            proposal.DecidedByUserId, proposal.DecidedAtUtc, proposal.DecisionReason, proposal.Version);
        return view with { CancellationId = cancellation.CancellationId, ReversalProposal = value,
            Status = view.Status == "CancellationReversalPending" && value?.Status == "Approved" ? "CancelledByReversal" : view.Status };
    }

    private static AccountingJournalReversalProposalViewV1 ProposalView(AccountingJournalReversalProposalV1 proposal) => new(
        proposal.ProposalId, proposal.CompanyId, proposal.SourceJournalId, proposal.ReversalJournalId, proposal.PeriodId,
        proposal.TransactionDate, proposal.AccountingDate, proposal.ValueDate, proposal.Reason, proposal.ProposedBy,
        proposal.ProposedAtUtc, proposal.Status.ToString(), proposal.DecidedBy, proposal.DecidedAtUtc, proposal.DecisionReason, proposal.Version);
}
