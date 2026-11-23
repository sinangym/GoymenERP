using System.Data;
using System.Globalization;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingInventoryCostStoreV1
{
    private async Task<(long Revision, IReadOnlyList<InventoryCostCancellationComponentV1> Pending)>
        CancellationEvidence(AccountingInventoryCostProposalV1 view, CancellationToken token)
    {
        if (await SupersedingProof(view, token) is { } successor)
            return (successor.InvoiceLineRevision, []); // The actual complete release is immutable; later deltas belong to its successor.
        var reader = new InvoiceLineCogsCorrectionStoreV1(db, company, plans, audit, auditWriter, clock,
            postedManufacturing, postedPurchasing);
        (long Revision, IReadOnlyList<InventoryCostCancellationComponentV1> Components) evidence;
        try { evidence = await reader.ReadCancellationComponentsAsync(view, token); }
        catch (AccountingStoreException exception) when (exception.Code == InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid)
        { throw Refuse("inventory_cost_source_unavailable"); }
        var claims = await db.InventoryCostCancellationClaims.AsNoTracking().Where(x => x.InvoiceId == view.InvoiceId
            && x.InvoiceLineId == view.Component.InvoiceLineId && x.FinalizedVersion == view.InvoiceVersion).ToListAsync(token);
        RequireCompany(view.CompanyId);
        foreach (var row in claims)
        {
            var component = Decode<InventoryCostCancellationComponentV1>(row.ViewJson);
            var current = evidence.Components.SingleOrDefault(x => x.EconomicKey == row.EconomicKey);
            var approvedRow = await db.InventoryCostCancellationVersions.AsNoTracking().SingleOrDefaultAsync(x =>
                x.InventoryCostProposalId == row.InventoryCostProposalId && x.Version == row.CancellationVersion, token);
            var approved = approvedRow is null ? null : ReadCancellationVersion(approvedRow, view);
            if (row.CompanyId != view.CompanyId || row.InventoryCostProposalId != view.ProposalId
                || row.Sha256 != AccountingInventoryCostSourceValidationV1.Sha256(row.ViewJson)
                || current is null || Encode(component with { ReversingJournalId = null }) != Encode(current)
                || component.EconomicKey != row.EconomicKey || component.SourceJournalId != row.SourceJournalId
                || component.ReversingJournalId != row.ReversingJournalId || approved is not { Status: "Approved" }
                || !approved.Components.Any(x => Encode(x) == Encode(component)))
                throw Refuse("inventory_cost_posting_conflict");
            await VerifyCancellationJournal(component, approved, token);
        }
        return (evidence.Revision, evidence.Components.Where(x => !claims.Any(c => c.EconomicKey == x.EconomicKey)).ToArray());
    }

    private async Task<AccountingInventoryCostProposalV1> ComponentCancellationState(AccountingInventoryCostProposalV1 view,
        AccountingSourceInvoiceCancellationRow cancellation, CancellationToken token)
    {
        await using var read = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
        var evidence = await CancellationEvidence(view, token);
        var latest = await LatestCancellation(view, token);
        var status = evidence.Pending.Count == 0 ? latest is { Status: "Approved" } ? "CancelledByComponentReversal"
                : view.Status == "RoundedNoCost" ? "RoundedNoCostCancelled" : "Cancelled"
            : latest is { Status: "Prepared" } ? "ComponentCancellationPrepared" : "ComponentCancellationPending";
        return view with { Status = status, CancellationId = cancellation.CancellationId, ComponentCancellation = latest };
    }

    private async Task<AccountingInventoryCostProposalV1> PrepareComponentCancellation(AccountingInventoryCostProposalV1 view,
        ProposeAccountingInventoryCostReversalV1 request, Guid actorId, Func<CancellationToken, Task> fence,
        AccountingCommandMetadata metadata, CancellationToken token)
    {
        var cancellation = await Cancellation(view, token) ?? throw Refuse("inventory_cost_reversal_not_proposable");
        if (request.ProposalId == Guid.Empty || request.TransactionDate == default || request.AccountingDate == default
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000)
            throw Refuse("inventory_cost_request_invalid");
        var latest = await LatestCancellation(view, token);
        if ((latest?.Version ?? 0) != (request.ExpectedCancellationVersion ?? 0))
            throw new AccountingVersionConflictException(request.ExpectedCancellationVersion ?? 0, latest?.Version ?? 0);
        if (await db.InventoryCostCancellationVersions.AnyAsync(x => x.Id == request.ProposalId, token))
            throw Refuse("inventory_cost_source_conflict");
        var evidence = await CancellationEvidence(view, token);
        if (evidence.Pending.Count == 0) throw Refuse("inventory_cost_reversal_not_proposable");
        await Period(request.PeriodId, request.AccountingDate, token);
        var profiles = await CancellationProfiles(view.CompanyId, request.AccountingDate, evidence.Pending, token);
        await fence(token);
        var prepared = new InventoryCostCancellationV1(request.ProposalId, view.CompanyId, view.ProposalId,
            cancellation.CancellationId, checked((latest?.Version ?? 0) + 1), "Prepared", evidence.Revision,
            request.PeriodId, request.AccountingDate, request.TransactionDate, request.Reason.Trim(), actorId,
            clock.GetUtcNow(), evidence.Pending, profiles);
        AddCancellation(view, prepared);
        var result = view with { Status = "ComponentCancellationPrepared", CancellationId = cancellation.CancellationId,
            ComponentCancellation = prepared };
        AuditComponentCancellation(prepared, latest, metadata, "prepared");
        return result;
    }

    public Task<AccountingInventoryCostProposalV1> ApproveCancellationAsync(Guid proposalId,
        ApproveInventoryCostCancellationV1 request, Guid actorId, Func<CancellationToken, Task> freshFence,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) => Atomic(metadata, freshFence, async () =>
    {
        Actor(actorId, metadata);
        var first = await db.InventoryCostProposals.AsNoTracking().Where(x => x.Id == proposalId).OrderBy(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken) ?? throw Refuse("inventory_cost_not_found");
        var identity = await ReadStored(first, cancellationToken);
        var cancelled = await Cancellation(identity, cancellationToken) ?? throw Refuse("inventory_cost_reversal_not_proposable");
        var row = await db.InventoryCostProposals.AsNoTracking().Where(x => x.Id == proposalId).OrderByDescending(x => x.Version).FirstAsync(cancellationToken);
        if (row.Version != request.ExpectedVersion) throw new AccountingVersionConflictException(request.ExpectedVersion, row.Version);
        var view = await ReadStored(row, cancellationToken);
        var prepared = await LatestCancellation(view, cancellationToken) ?? throw Refuse("inventory_cost_reversal_not_proposable");
        if (prepared.Version != request.ExpectedCancellationVersion)
            throw new AccountingVersionConflictException(request.ExpectedCancellationVersion, prepared.Version);
        if (prepared.Status != "Prepared" || prepared.PreparedBy == actorId || prepared.CancellationId != cancelled.CancellationId)
            throw Refuse("inventory_cost_approval_refused");
        var evidence = await CancellationEvidence(view, cancellationToken);
        if (prepared.InvoiceLineRevision != evidence.Revision || Encode(prepared.Components) != Encode(evidence.Pending)
            || evidence.Pending.Count == 0) throw Refuse("inventory_cost_preparation_stale");
        await Period(prepared.PeriodId, prepared.AccountingDate, cancellationToken);
        var profiles = await CancellationProfiles(view.CompanyId, prepared.AccountingDate, evidence.Pending, cancellationToken);
        if (Encode(profiles) != Encode(prepared.MoneyProfiles)) throw Refuse("inventory_cost_preparation_stale");
        var period = await db.Periods.SingleAsync(x => x.Id == prepared.PeriodId, cancellationToken);
        var restored = AccountingPeriod.Restore(period.Id, period.CompanyId, period.StartsOn, period.EndsOn,
            period.PreparedByUserId, period.PreparedAtUtc, period.ApprovedByUserId, period.ApprovedAtUtc,
            period.ClosedByUserId, period.ClosedAtUtc);
        var answered = new List<InventoryCostCancellationComponentV1>();
        foreach (var item in prepared.Components)
        {
            var signed = ReadCancellationAmount(item.SignedAmount);
            if (signed == 0) { answered.Add(item); continue; }
            var original = await ReadCancellationSourceJournal(item.SourceJournalId!.Value, cancellationToken);
            var proof = new PostedCogsCancellationComponent(view.CompanyId, original.Id, item.JournalSourceId,
                item.PostingPurpose, item.CurrencyId, ReadCancellationAmount(item.JournalTotal), signed,
                item.CogsAccountId, item.CounterAccountId, item.EconomicKey, item.SourceFingerprint);
            var journal = InvoiceLineCogsCancellation.ReverseComponent(original, proof, restored,
                new(prepared.TransactionDate, prepared.AccountingDate, null, clock.GetUtcNow()), Guid.NewGuid());
            AddCancellationJournal(journal, prepared.PeriodId, metadata);
            answered.Add(item with { ReversingJournalId = journal.Id });
        }
        await freshFence(cancellationToken);
        var approved = prepared with { Version = checked(prepared.Version + 1), Status = "Approved", Components = answered,
            ApprovedBy = actorId, ApprovedAtUtc = clock.GetUtcNow() };
        AddCancellation(view, approved);
        foreach (var item in answered)
        {
            var json = Encode(item);
            db.InventoryCostCancellationClaims.Add(new() { CompanyId = view.CompanyId, InvoiceId = view.InvoiceId,
                InvoiceLineId = view.Component.InvoiceLineId!.Value, FinalizedVersion = view.InvoiceVersion,
                EconomicKey = item.EconomicKey, InventoryCostProposalId = view.ProposalId, CancellationVersion = approved.Version,
                SourceJournalId = item.SourceJournalId, ReversingJournalId = item.ReversingJournalId, ViewJson = json,
                Sha256 = AccountingInventoryCostSourceValidationV1.Sha256(json), RecordedAtUtc = clock.GetUtcNow() });
        }
        // The same owner period row serializes closing, including a zero-only acknowledgement with no journal.
        period.Version = checked(period.Version + 1); period.UpdatedAtUtc = clock.GetUtcNow();
        db.Entry(period).Property(x => x.UpdatedAtUtc).IsModified = true;
        var result = view with { Status = "CancelledByComponentReversal", CancellationId = cancelled.CancellationId,
            ComponentCancellation = approved };
        AuditComponentCancellation(approved, prepared, metadata, "approved");
        return result;
    }, cancellationToken);

    private async Task<IReadOnlyList<InventoryCostCancellationMoneyProfileV1>> CancellationProfiles(Guid companyId,
        DateOnly date, IReadOnlyList<InventoryCostCancellationComponentV1> parts, CancellationToken token)
    {
        var result = new List<InventoryCostCancellationMoneyProfileV1>();
        foreach (var group in parts.GroupBy(x => x.CurrencyId).OrderBy(x => x.Key))
        {
            var profile = await amounts.ReadProfileAsync(companyId, group.Key, date, token);
            RequireCompany(companyId);
            if (group.Any(x => { var value = ReadCancellationAmount(x.SignedAmount);
                return decimal.Round(value, profile.Scale) != value || value != 0 && x.SourceJournalId is null; }))
                throw Refuse("inventory_cost_amount_unaccepted");
            result.Add(new(group.Key, profile.Profile.ProfileId, profile.Profile.Version, profile.Scale));
        }
        return result;
    }

    private async Task<InventoryCostCancellationV1?> LatestCancellation(AccountingInventoryCostProposalV1 view, CancellationToken token)
    {
        var row = await db.InventoryCostCancellationVersions.AsNoTracking().Where(x => x.InventoryCostProposalId == view.ProposalId)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
        RequireCompany(view.CompanyId);
        return row is null ? null : ReadCancellationVersion(row, view);
    }

    private static InventoryCostCancellationV1 ReadCancellationVersion(InventoryCostCancellationVersionRow row,
        AccountingInventoryCostProposalV1 source)
    {
        var view = Decode<InventoryCostCancellationV1>(row.ViewJson);
        if (row.CompanyId != source.CompanyId || row.InventoryCostProposalId != source.ProposalId
            || view.CompanyId != row.CompanyId || view.InventoryCostProposalId != row.InventoryCostProposalId
            || view.Id != row.Id || view.Version != row.Version || view.CancellationId != row.CancellationId
            || view.PreparedBy != row.PreparedBy || view.ApprovedBy != row.ApprovedBy || view.PeriodId != row.PeriodId
            || row.Sha256 != AccountingInventoryCostSourceValidationV1.Sha256(row.ViewJson)
            || view.Status != (row.Status == 1 ? "Prepared" : row.Status == 2 ? "Approved" : "")
            || view.Components is not { Count: > 0 } || view.MoneyProfiles is not { Count: > 0 }
            || view.Status == "Approved" && (view.ApprovedBy is null || view.ApprovedBy == view.PreparedBy || view.ApprovedAtUtc is null))
            throw Refuse("inventory_cost_source_conflict");
        return view;
    }

    private void AddCancellation(AccountingInventoryCostProposalV1 source, InventoryCostCancellationV1 view)
    {
        var json = Encode(view);
        db.InventoryCostCancellationVersions.Add(new() { CompanyId = source.CompanyId, InventoryCostProposalId = source.ProposalId,
            InitialProposalVersion = source.Version, Id = view.Id, Version = view.Version, CancellationId = view.CancellationId,
            Status = view.Status == "Prepared" ? 1 : 2, PreparedBy = view.PreparedBy, ApprovedBy = view.ApprovedBy,
            PeriodId = view.PeriodId, ViewJson = json, Sha256 = AccountingInventoryCostSourceValidationV1.Sha256(json),
            RecordedAtUtc = clock.GetUtcNow() });
    }

    private static decimal ReadCancellationAmount(string text) => decimal.TryParse(text,
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
        && amount.ToString("0.############################", CultureInfo.InvariantCulture) == text
        ? amount : throw Refuse("inventory_cost_posting_conflict");

    private async Task<Journal> ReadCancellationSourceJournal(Guid id, CancellationToken token)
    {
        var row = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw Refuse("inventory_cost_posting_conflict");
        var lines = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == id).OrderBy(x => x.LineNumber).ToListAsync(token);
        return Journal.Restore(row.Id, (JournalKind)row.Kind, new(row.CompanyId, row.SourceId, row.PostingPurpose),
            row.PostingRuleVersion, row.CurrencyId, new(row.TransactionDate, row.AccountingDate, row.ValueDate, row.RecordedAtUtc),
            lines.Select(x => new JournalLine(new(x.CompanyId, x.AccountId), x.Debit, x.Credit)), row.LinkedJournalId);
    }

    private async Task VerifyCancellationJournal(InventoryCostCancellationComponentV1 part, InventoryCostCancellationV1 approval,
        CancellationToken token)
    {
        var signed = ReadCancellationAmount(part.SignedAmount);
        if (signed == 0)
        {
            if (part.ReversingJournalId.HasValue) throw Refuse("inventory_cost_posting_conflict");
            return;
        }
        if (part.SourceJournalId is null || part.ReversingJournalId is not { } id) throw Refuse("inventory_cost_posting_conflict");
        var journal = await ReadCancellationSourceJournal(id, token);
        var positive = signed > 0; var value = Math.Abs(signed);
        if (journal.Kind != JournalKind.Correction || journal.LinkedJournalId != part.SourceJournalId
            || journal.Source.SourceId != part.JournalSourceId || journal.Source.PostingPurpose != part.PostingPurpose
            || journal.PostingRuleVersion != InvoiceLineCogsCancellation.RuleVersion || journal.CurrencyId != part.CurrencyId
            || journal.Dates.AccountingDate != approval.AccountingDate || journal.Dates.TransactionDate != approval.TransactionDate
            || journal.Lines.Count != 2 || journal.Lines[0].Debit != value || journal.Lines[0].Credit != 0
            || journal.Lines[1].Credit != value || journal.Lines[1].Debit != 0
            || journal.Lines[positive ? 1 : 0].Account.AccountId != part.CogsAccountId
            || journal.Lines[positive ? 0 : 1].Account.AccountId != part.CounterAccountId)
            throw Refuse("inventory_cost_posting_conflict");
    }

    private void AddCancellationJournal(Journal journal, Guid periodId, AccountingCommandMetadata metadata)
    {
        db.Journals.Add(new() { CompanyId = journal.CompanyId, Id = journal.Id, Kind = (int)journal.Kind,
            SourceId = journal.Source.SourceId, PostingPurpose = journal.Source.PostingPurpose, PostingRuleVersion = journal.PostingRuleVersion,
            CurrencyId = journal.CurrencyId, TransactionDate = journal.Dates.TransactionDate, AccountingDate = journal.Dates.AccountingDate,
            ValueDate = journal.Dates.ValueDate, RecordedAtUtc = journal.Dates.RecordedAtUtc, LinkedJournalId = journal.LinkedJournalId,
            CorrectionId = Guid.NewGuid(), PeriodId = periodId, StoredAtUtc = clock.GetUtcNow(), StoredBy = metadata.ActorId,
            CorrelationId = metadata.CorrelationId });
        db.JournalLines.AddRange(journal.Lines.Select((line, i) => new JournalLineRow { CompanyId = journal.CompanyId,
            JournalId = journal.Id, LineNumber = i + 1, AccountId = line.Account.AccountId, Debit = line.Debit, Credit = line.Credit }));
    }

    private void AuditComponentCancellation(InventoryCostCancellationV1 view, InventoryCostCancellationV1? previous,
        AccountingCommandMetadata metadata, string action) => audit.Enqueue(new AuditEventDraft(view.CompanyId,
            "accounting.inventory-cost.component-cancellation-" + action + ".v1", "Accounting.InventoryCostCancellation",
            view.InventoryCostProposalId.ToString("D"), action, previous?.Version, view.Version, AuditActorTypes.User,
            metadata.ActorId, metadata.PermissionCode, metadata.RoleCodes, previous is null ? "{}" : Encode(previous),
            Encode(view), view.Reason, Encode(new { view.CancellationId, view.PreparedBy, view.ApprovedBy }),
            AuditAiInvolvement.None, null, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
}
