using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingSourceVoucherStoreV1(AccountingDbContext db, AccountingStore posting,
    ICompanyContext company, IActiveAccountPlanReaderV1 accountPlans, ISettingsMoneyCandidateReader currencies,
    IAuditEventCollector audit, TimeProvider clock) : IAccountingSourceVoucherStoreV1
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AccountingSourceVoucherViewV1>> ListAsync(CancellationToken cancellationToken = default)
    {
        var companyId = Company();
        var rows = await db.SourceDocumentVoucherDrafts.AsNoTracking().OrderByDescending(x => x.DocumentDate)
            .ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var lines = await db.SourceDocumentVoucherDraftLines.AsNoTracking().OrderBy(x => x.LineNumber).ToListAsync(cancellationToken);
        var cancellations = await db.SourceInvoiceCancellations.AsNoTracking().ToListAsync(cancellationToken);
        var reversals = await db.JournalReversalProposals.AsNoTracking().ToListAsync(cancellationToken);
        var plan = await accountPlans.ReadActiveAsync(cancellationToken);
        var periods = await db.Periods.AsNoTracking().Where(x => x.Status == 1).ToListAsync(cancellationToken);
        RequireCompany(companyId);
        return rows.Select(row =>
        {
            var cancellation = cancellations.SingleOrDefault(x => x.SourceDocumentId == row.SourceDocumentId);
            var view = WithCancellation(View(row, lines.Where(line => line.DraftId == row.Id), cancellation?.Reason),
                cancellation, reversals.Where(x => x.SourceJournalId == row.JournalId));
            return view with
            {
                Status = row.Status == 1 ? periods.Any(x => x.StartsOn <= row.DocumentDate && x.EndsOn >= row.DocumentDate)
                    ? "AwaitingApproval" : "PeriodWaiting" : view.Status,
                Lines = view.Lines.Select(line =>
                {
                    var account = plan?.CompanyId == companyId && plan.Status == ApprovedAccountingSourceCatalogStatusV1.Active
                        ? plan.Package.Mirror.Lookup(new AccountRef(companyId, line.AccountId)) : null;
                    return line with { AccountCode = account?.Code, AccountName = account?.Name };
                }).ToArray(),
            };
        }).ToArray();
    }

    public async Task<AccountingSourceVoucherViewV1?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var companyId = Company();
        var row = await db.SourceDocumentVoucherDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null) { RequireCompany(companyId); return null; }
        var lines = await db.SourceDocumentVoucherDraftLines.AsNoTracking().Where(x => x.DraftId == id)
            .OrderBy(x => x.LineNumber).ToListAsync(cancellationToken);
        var cancellation = await db.SourceInvoiceCancellations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SourceDocumentId == row.SourceDocumentId, cancellationToken);
        var reversals = await db.JournalReversalProposals.AsNoTracking().Where(x => x.SourceJournalId == row.JournalId)
            .ToListAsync(cancellationToken);
        RequireCompany(companyId);
        return WithCancellation(View(row, lines, cancellation?.Reason), cancellation, reversals);
    }

    public async Task<AccountingSourceVoucherViewV1> ApproveAsync(Guid id, long expectedVersion, Guid actorId,
        Func<CancellationToken, Task> freshAuthorityFence, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var companyId = Company();
        if (actorId == Guid.Empty || !Guid.TryParse(metadata.ActorId, out var auditActor) || auditActor != actorId)
            throw Refuse("source_voucher_approval_refused");
        await freshAuthorityFence(cancellationToken);
        var source = await db.SourceDocumentVoucherDrafts.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.SourceDocumentId }).SingleOrDefaultAsync(cancellationToken)
            ?? throw Refuse("source_voucher_not_found");
        RequireCompany(companyId);
        // The source-cancellation key range and the accounting period remain protected through the journal commit.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var cancelled = await db.SourceInvoiceCancellations.AnyAsync(x => x.SourceDocumentId == source.SourceDocumentId, cancellationToken);
            var row = await db.SourceDocumentVoucherDrafts.SingleAsync(x => x.Id == id, cancellationToken);
            if (expectedVersion < 1 || row.Version != expectedVersion) throw new AccountingVersionConflictException(expectedVersion, row.Version);
            AccountingSourceVoucherRulesV1.RequireApproval(row.Status, row.SourceActorId, actorId, cancelled);
            var period = await db.Periods.SingleOrDefaultAsync(x => x.Status == 1 && x.StartsOn <= row.DocumentDate
                && x.EndsOn >= row.DocumentDate, cancellationToken);
            if (period is null) throw Refuse("source_voucher_period_waiting");
            var lines = await db.SourceDocumentVoucherDraftLines.AsNoTracking().Where(x => x.DraftId == id)
                .OrderBy(x => x.LineNumber).ToListAsync(cancellationToken);
            var plan = await accountPlans.ReadActiveAsync(cancellationToken);
            RequireCompany(companyId);
            if (plan is null || plan.CompanyId != companyId || plan.Status != ApprovedAccountingSourceCatalogStatusV1.Active
                || plan.Package.Mirror.CompanyId != companyId) throw Refuse("source_voucher_account_plan_missing");
            if (lines.Count == 0 || lines.Any(line => plan.Package.Mirror.Lookup(new AccountRef(companyId, line.AccountId)) is null))
                throw Refuse("source_voucher_account_not_in_plan");
            var currency = MoneyCurrencyCandidatesV1.EnsureWellFormed(await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken))
                .Where(x => x.Code == "TRY").ToArray();
            if (currency.Length != 1) throw Refuse("source_voucher_tl_currency_unavailable");
            await freshAuthorityFence(cancellationToken);
            RequireCompany(companyId);
            var before = JsonSerializer.Serialize(View(row, lines, null), Json);
            var request = new PostingRequest("accounting.invoice-journal", row.Id.ToString("D"), row.Id,
                new SourceReference(companyId, row.SourceDocumentId, "invoice-source"),
                "invoice-mapping/" + row.MappingVersion.ToString(CultureInfo.InvariantCulture), currency[0].CurrencyId,
                new PostingDates(row.DocumentDate, row.DocumentDate, null, clock.GetUtcNow()),
                lines.Select(line => new ProposedLine(line.MappingKey, new AccountRef(companyId, line.AccountId),
                    (PostingSide)line.Side, line.Amount)).ToArray());
            var outcome = await posting.ReceiveAsync(request, period.Id, metadata with { Completion = null }, cancellationToken);
            if (outcome.Kind != PostingOutcomeKind.Posted || outcome.Journal?.Id != row.Id)
                throw Refuse("source_voucher_posting_conflict");
            await freshAuthorityFence(cancellationToken);
            RequireCompany(companyId);
            var version = row.Version;
            row.Status = 2;
            row.PeriodId = period.Id;
            row.JournalId = outcome.Journal.Id;
            row.ApprovedBy = actorId;
            row.ApprovedAtUtc = clock.GetUtcNow();
            row.UpdatedAtUtc = row.ApprovedAtUtc.Value;
            row.Version = checked(version + 1);
            var result = View(row, lines, null);
            audit.Enqueue(new AuditEventDraft(companyId, "accounting.source-voucher.approved.v1", "Accounting.SourceVoucher",
                row.Id.ToString("D"), "Approve", version, row.Version, AuditActorTypes.User, metadata.ActorId,
                metadata.PermissionCode, metadata.RoleCodes, before, JsonSerializer.Serialize(result, Json),
                "Kesinleşmiş kaynak faturanın taslak fişi ayrı muhasebe yetkilisince onaylandı.", null,
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
            || exception is DbUpdateException { InnerException: SqlException { Number: 1205 } })
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

    internal static AccountingSourceVoucherViewV1 View(AccountingSourceDocumentVoucherDraftRow row,
        IEnumerable<AccountingSourceDocumentVoucherDraftLineRow> lines, string? cancellationReason) => new AccountingSourceVoucherViewV1(row.Id,
        row.CompanyId, row.DocumentKind, row.SourceDocumentId, row.InvoiceId, row.FinalizedVersion, row.SourceActorId,
        row.PartyId, row.CurrencyId, row.ExchangeRate, row.DocumentDate, row.DueDate,
        row.Status switch { 1 => row.PeriodId is null ? "PeriodWaiting" : "AwaitingApproval",
            2 => cancellationReason is null ? "Posted" : "CancellationReversalPending", 3 => "Cancelled", _ => throw new InvalidOperationException() },
        row.Version, row.MappingVersion, row.PeriodId, row.JournalId, row.ApprovedBy, row.ApprovedAtUtc,
        cancellationReason, lines.Select(line => new AccountingSourceVoucherLineViewV1(line.LineNumber, line.MappingKey,
            line.Side, line.AccountId, line.Amount.ToString("0.############################", CultureInfo.InvariantCulture),
            line.SourceLineNumber, line.VatRateCode)).ToArray(), row.OfficialInvoiceNo,
        row.FunctionalPayable.ToString("0.############################", CultureInfo.InvariantCulture));

    private Guid Company() => company.IsResolved && company.CompanyId != Guid.Empty ? company.CompanyId
        : throw Refuse(AccountingStoreCodes.CompanyContextMismatch);
    private void RequireCompany(Guid captured) { if (Company() != captured) throw Refuse(AccountingStoreCodes.CompanyContextMismatch); }
    private static AccountingStoreException Refuse(string code) => new AccountingStoreException(code, "Kaynak fatura fişi işlemi tamamlanamadı.");
}
