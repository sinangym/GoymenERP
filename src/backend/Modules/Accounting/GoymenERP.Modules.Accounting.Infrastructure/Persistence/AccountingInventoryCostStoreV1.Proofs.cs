using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingInventoryCostStoreV1
{
    private async Task<AccountingInventoryCostProposalV1> ReadStored(AccountingInventoryCostProposalRow row, CancellationToken token)
    {
        AccountingInventoryCostProposalV1 view;
        AccountingInventoryCostSourceV1 source;
        var header = await db.InventoryCostSources.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == row.EventId, token)
            ?? throw Refuse("inventory_cost_source_unavailable");
        try { view = Decode<AccountingInventoryCostProposalV1>(row.ViewJson); source = Decode<AccountingInventoryCostSourceV1>(header.SourceJson); }
        catch (System.Text.Json.JsonException) { throw Refuse("inventory_cost_source_unavailable"); }
        var component = source.Components?.SingleOrDefault(x => x.ComponentKey == row.ComponentKey);
        var status = row.Status switch { 1 => "AwaitingPreparation", 2 => "Prepared", 3 => "Posted", 4 => "NoCost", 5 => "AlreadyAccounted", 6 => "RoundedNoCost", _ => "" };
        if (header.CompanyId != Company() || source.CompanyId != header.CompanyId || source.EventId != header.EventId
            || (int)source.Kind != header.Kind || source.InvoiceId != header.InvoiceId || source.InvoiceVersion != header.InvoiceVersion
            || source.AdmissionId != header.AdmissionId || source.SourcePayloadSha256 != header.PayloadHash
            || row.CompanyId != header.CompanyId || view.ProposalId != row.Id || view.CompanyId != row.CompanyId
            || view.EventId != row.EventId || view.Version != row.Version || view.Status != status
            || view.Kind != source.Kind || view.InvoiceId != source.InvoiceId || view.InvoiceVersion != source.InvoiceVersion
            || view.AdmissionId != source.AdmissionId || view.InvoiceDate != source.InvoiceDate || view.OfficialInvoiceNumber != source.OfficialInvoiceNumber
            || view.SourceOccurredAtUtc != header.OccurredAtUtc || component is null || Encode(view.Component) != Encode(component)
            || component.SourceProofSha256 != AccountingInventoryCostSourceValidationV1.Sha256(component.SourceProofJson)
            || !AccountingInventoryCostAmountsV1.TryRead(component.SignedTlAmount, out _, out _)
            || view.Preparation?.PreparedBy != row.PreparedBy || view.Preparation?.PeriodId != row.PeriodId
            || view.ApprovedBy != row.ApprovedBy || view.JournalId != row.JournalId || view.CancellationId is not null || view.ReversalProposal is not null)
            throw Refuse("inventory_cost_source_unavailable");
        if (view.Preparation?.Rounding is { } rounding && (!AccountingInventoryCostRoundingV1Rules.TryResolve(component,
                view.Preparation.CurrencyId, view.Preparation.MoneyProfileId, view.Preparation.MoneyProfileVersion,
                view.Preparation.MoneyScale, out _, out var expectedRounding)
            || rounding != expectedRounding || rounding.RoundedSignedAmount != view.Preparation.SignedAmount)
            || component.RecordedRounding is { } sourceRounding && !AccountingInventoryCostRoundingV1Rules.IsValidRecorded(component.SignedTlAmount, sourceRounding)
            || status == "RoundedNoCost" && view.Preparation is not { Rounding: not null, SignedAmount: "0" })
            throw Refuse("inventory_cost_source_unavailable");
        return view;
    }

    private async Task<AccountingInventoryCostClassificationV1> Classification(AccountingInventoryCostProposalV1 view,
        DateOnly businessDate, CancellationToken token)
    {
        // An already approved invoice-line choice is authoritative even before the first COGS journal is posted.
        // Product is only a default. Steel/scrap and purchase/manufacturing history never supply this classification.
        if (view.Kind == AccountingInventoryCostKindV1.SalesInvoicedShipment)
        {
            var latest = await db.InvoiceLineCogsRevisions.AsNoTracking().Where(x => x.InvoiceId == view.InvoiceId
                && x.InvoiceLineId == view.Component.InvoiceLineId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
            if (latest is not null)
            {
                var revision = Decode<InvoiceLineCogsRevisionViewV1>(latest.RevisionJson);
                return new(revision.Classification, revision.AccountPlan.Account.AccountId, "InvoiceLine", null, null, null, revision.Version);
            }
        }
        var read = await cogs.ResolveAsync(view.CompanyId, view.Component.ProductId, businessDate, token);
        RequireCompany(view.CompanyId);
        if (read.Outcome != "Available" || read.Value is not { } value || value.CompanyId != view.CompanyId
            || value.ProductId != view.Component.ProductId) throw Refuse(read.Outcome == "ClassificationMissing"
                ? "inventory_cost_classification_missing" : "inventory_cost_mapping_missing");
        return new(value.Classification, value.AccountId, "Product", value.ProductVersion, value.ClassificationVersion, value.MappingVersion, 0);
    }

    private async Task<ApprovedAccountingSourceCatalogRecordV1> Plan(CancellationToken token)
    {
        var captured = Company();
        var plan = await plans.ReadActiveAsync(token);
        RequireCompany(captured);
        if (plan is null || plan.CompanyId != captured || plan.Status != ApprovedAccountingSourceCatalogStatusV1.Active
            || plan.Package.Mirror.CompanyId != captured) throw Refuse("inventory_cost_account_plan_missing");
        return plan;
    }

    private async Task Period(Guid id, DateOnly date, CancellationToken token)
    {
        var period = await db.Periods.SingleOrDefaultAsync(x => x.Id == id, token);
        if (date == default || period is null || period.Status != 1 || date < period.StartsOn || date > period.EndsOn)
            throw Refuse("inventory_cost_open_period_required");
    }

    private async Task<Guid> TlCurrency(CancellationToken token)
    {
        var captured = Company();
        var candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(await currencies.GetMoneyCurrencyCandidatesAsync(token));
        RequireCompany(captured);
        var tl = candidates.Where(x => x.Code == "TRY").ToArray();
        return tl.Length == 1 ? tl[0].CurrencyId : throw Refuse("inventory_cost_tl_currency_unavailable");
    }

    private async Task<AccountingSourceInvoiceCancellationRow?> Cancellation(AccountingInventoryCostProposalV1 view, CancellationToken token)
    {
        if (view.Kind != AccountingInventoryCostKindV1.SalesInvoicedShipment) return null;
        var row = await db.SourceInvoiceCancellations.AsNoTracking().SingleOrDefaultAsync(x => x.SourceDocumentId == view.AdmissionId, token);
        if (row is not null && (row.CompanyId != view.CompanyId || row.InvoiceId != view.InvoiceId
            || row.FinalizedVersion != view.InvoiceVersion || row.DocumentKind != AccountingSourceDocumentKindsV1.SalesInvoice))
            throw Refuse("inventory_cost_source_conflict");
        return row;
    }

    private async Task<Guid> CurrentPostedJournal(AccountingInventoryCostProposalV1 view, CancellationToken token)
    {
        var journal = view.JournalId ?? throw Refuse("inventory_cost_posting_conflict");
        if (view.Kind == AccountingInventoryCostKindV1.SalesInvoicedShipment)
        {
            var revisions = await db.InvoiceLineCogsRevisions.AsNoTracking().Where(x => x.InvoiceId == view.InvoiceId
                && x.InvoiceLineId == view.Component.InvoiceLineId).OrderByDescending(x => x.Version).ToListAsync(token);
            var replacement = revisions.Select(x => Decode<InvoiceLineCogsRevisionViewV1>(x.RevisionJson))
                .FirstOrDefault(x => x.ReplacementCorrectionJournalId.HasValue)?.ReplacementCorrectionJournalId;
            if (replacement is { } corrected) journal = corrected;
        }
        var row = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == journal, token);
        var purpose = view.Kind == AccountingInventoryCostKindV1.SalesInvoicedShipment
            ? AccountingInventoryCostTraceV1.SalesPurpose(view.Component.InvoiceLineId!.Value, view.InvoiceVersion)
            : AccountingInventoryCostTraceV1.PurchaseDifferencePurpose;
        var source = view.Kind == AccountingInventoryCostKindV1.SalesInvoicedShipment ? view.InvoiceId : view.ProposalId;
        if (row is null || row.SourceId != source || row.PostingPurpose != purpose) throw Refuse("inventory_cost_posting_conflict");
        return journal;
    }

    private async Task<AccountingInventoryCostProposalV1> CurrentState(AccountingInventoryCostProposalV1 view, CancellationToken token)
    {
        var cancelled = await Cancellation(view, token);
        if (cancelled is null) return view;
        if (view.Component.AlreadyPostedComponents is { Count: > 0 })
            return view with { Status = "AttributedCancellationPending", CancellationId = cancelled.CancellationId };
        if (view.JournalId is null) return view with { Status = view.Status == "RoundedNoCost" ? "RoundedNoCostCancelled" : "Cancelled", CancellationId = cancelled.CancellationId };
        var journal = await CurrentPostedJournal(view, token);
        var reversals = await db.JournalReversalProposals.AsNoTracking().Where(x => x.SourceJournalId == journal).ToListAsync(token);
        var proposal = reversals.OrderByDescending(x => x.Status == (int)AccountingJournalReversalProposalStatusV1.Approved)
            .ThenByDescending(x => x.Status == (int)AccountingJournalReversalProposalStatusV1.Pending)
            .ThenByDescending(x => x.ProposedAtUtc).FirstOrDefault();
        var reversal = proposal is null ? null : new AccountingJournalReversalProposalViewV1(proposal.Id, proposal.CompanyId,
            proposal.SourceJournalId, proposal.ReversalJournalId, proposal.PeriodId, proposal.TransactionDate, proposal.AccountingDate,
            proposal.ValueDate, proposal.Reason, proposal.ProposedByUserId, proposal.ProposedAtUtc,
            ((AccountingJournalReversalProposalStatusV1)proposal.Status).ToString(), proposal.DecidedByUserId, proposal.DecidedAtUtc,
            proposal.DecisionReason, proposal.Version);
        var isReversed = await db.Journals.AnyAsync(x => x.LinkedJournalId == journal && x.Kind == (int)JournalKind.Reversal, token);
        return view with { Status = isReversed ? "CancelledByReversal" : "CancellationReversalPending",
            CancellationId = cancelled.CancellationId, ReversalProposal = reversal, CurrentPostedJournalId = journal };
    }

    public Task<AccountingInventoryCostProposalV1> ProposeReversalAsync(Guid proposalId, ProposeAccountingInventoryCostReversalV1 request,
        Guid actorId, Func<CancellationToken, Task> freshFence, AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        Atomic(metadata, freshFence, async () =>
        {
            Actor(actorId, metadata);
            var first = await db.InventoryCostProposals.AsNoTracking().Where(x => x.Id == proposalId).OrderBy(x => x.Version)
                .FirstOrDefaultAsync(cancellationToken) ?? throw Refuse("inventory_cost_not_found");
            var source = await ReadStored(first, cancellationToken);
            if (source.Kind != AccountingInventoryCostKindV1.SalesInvoicedShipment || await Cancellation(source, cancellationToken) is null)
                throw Refuse("inventory_cost_reversal_not_proposable");
            var row = await db.InventoryCostProposals.AsNoTracking().Where(x => x.Id == proposalId).OrderByDescending(x => x.Version).FirstAsync(cancellationToken);
            if (request.ExpectedVersion != row.Version) throw new AccountingVersionConflictException(request.ExpectedVersion, row.Version);
            var view = await ReadStored(row, cancellationToken);
            if (view.Status != "Posted") throw Refuse("inventory_cost_reversal_not_proposable");
            var journal = await CurrentPostedJournal(view, cancellationToken);
            if (await db.JournalReversalProposals.AnyAsync(x => x.SourceJournalId == journal
                && (x.Status == (int)AccountingJournalReversalProposalStatusV1.Pending || x.Status == (int)AccountingJournalReversalProposalStatusV1.Approved), cancellationToken)
                || await db.Journals.AnyAsync(x => x.LinkedJournalId == journal && x.Kind == (int)JournalKind.Reversal, cancellationToken))
                throw Refuse("inventory_cost_reversal_not_proposable");
            await Period(request.PeriodId, request.AccountingDate, cancellationToken);
            var proposal = AccountingJournalReversalPolicyV1.Propose(Company(), actorId,
                new(request.ProposalId, journal, request.PeriodId, request.TransactionDate, request.AccountingDate, null, request.Reason), clock.GetUtcNow());
            await posting.CreateAsync(proposal, freshFence, metadata with { Completion = null, FreshAuthorityFence = freshFence }, cancellationToken);
            var result = await CurrentState(view, cancellationToken);
            Audit(result, metadata, "cancellation-reversal-proposed", view);
            return result;
        }, cancellationToken);
}
