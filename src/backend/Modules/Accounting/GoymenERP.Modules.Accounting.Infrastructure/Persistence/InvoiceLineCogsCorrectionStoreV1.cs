using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>Append-only line choices and component corrections, never a Sales/Inventory write or invoice-total reversal.</summary>
public sealed partial class InvoiceLineCogsCorrectionStoreV1(AccountingDbContext db, ICompanyContext company,
    IActiveAccountPlanReaderV1 accountPlans, IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter,
    TimeProvider clock, IPostedManufacturingShipmentCostReaderV1? postedManufacturing = null,
    IPostedPurchaseShipmentCostReaderV1? postedPurchasing = null) : IInvoiceLineCogsCorrectionStoreV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Entity = "Accounting.InvoiceLineCogsCorrection";

    public async Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken cancellationToken = default)
    {
        var captured = Company();
        var plan = await accountPlans.ReadActiveAsync(cancellationToken);
        if (plan is null || plan.CompanyId != captured || plan.Status != ApprovedAccountingSourceCatalogStatusV1.Active || plan.Package.Mirror.CompanyId != captured)
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.AccountPlanUnavailable);
        var periods = await db.Periods.AsNoTracking().Where(x => x.Status == (int)AccountingPeriodStatus.Open)
            .OrderBy(x => x.StartsOn).Select(x => new InvoiceLineCogsPeriodOptionV1(x.Id, x.StartsOn, x.EndsOn)).ToListAsync(cancellationToken);
        RequireCompany(captured);
        return new(captured, plan.Package.Mirror.Rows.OrderBy(x => x.Code, StringComparer.Ordinal)
            .Select(x => new InvoiceLineCogsAccountV1(x.Account.AccountId, x.Code, x.Name)).ToArray(), periods);
    }

    public async Task RecordAttemptAsync(AccountingCommandRecord record, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default)
    {
        var captured = Company();
        if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || db.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts) throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable);
        var started = false;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            started = true;
            await freshFence(cancellationToken);
            db.Commands.Add(new() { CompanyId = captured, CommandName = record.CommandName,
                IdempotencyKeyHash = record.IdempotencyKeyHash, ActorId = record.ActorId, PayloadHash = record.RequestHash,
                ResultJson = record.ResultJson, CorrelationId = record.OriginalCorrelationId, RecordedAtUtc = clock.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
            await freshFence(cancellationToken);
            RequireCompany(captured);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        { throw Refuse(AccountingStoreCodes.ConcurrentChange); }
        catch (DbUpdateException) { throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable); }
        catch (System.Data.Common.DbException) { throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable); }
        finally { if (started) db.ChangeTracker.Clear(); }
    }

    public async Task<InvoiceLineCogsHistoryV1> ReadAsync(FinalizedInvoiceLineSourceV1 source, CancellationToken cancellationToken = default)
    {
        var companyId = ValidateSource(source);
        await using var attributionRead = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        var revisions = await Revisions(source).AsNoTracking().OrderBy(x => x.Version).ToListAsync(cancellationToken);
        var proposals = await db.InvoiceLineCogsProposals.AsNoTracking().Where(x => x.InvoiceId == source.InvoiceId
            && x.InvoiceLineId == source.InvoiceLineId).OrderBy(x => x.PreparedAtUtc).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        if (proposals.Any(x => x.SourceJson != Encode(source))) throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceChanged);
        var approved = revisions.ToDictionary(x => x.ProposalId);
        var history = revisions.Select(x => Decode<InvoiceLineCogsRevisionViewV1>(x.RevisionJson)).ToArray();
        var (state, evidence) = await Evidence(source, history.LastOrDefault(), cancellationToken);
        var attributed = await AttributedEvidence(source, cancellationToken);
        var purchased = await PurchaseAttributedEvidence(source, cancellationToken);
        if (attributed is { Count: > 0 } || purchased is { Count: > 0 })
        {
            if (state == "Unreadable") throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            await RequireMixedPosting(source, evidence, cancellationToken);
            state = evidence is null ? "AttributedPostedCogs" : "MixedPostedCogs";
        }
        RequireCompany(companyId);
        return new(companyId, source.InvoiceId, source.InvoiceLineId, FinalizedInvoiceLineReferenceV1.From(source), history.LastOrDefault()?.Version ?? 0,
            history.LastOrDefault(), history, proposals.Select(x => approved.TryGetValue(x.Id, out var decision)
                ? Decode<InvoiceLineCogsCorrectionViewV1>(decision.ViewJson) : Decode<InvoiceLineCogsCorrectionViewV1>(x.ViewJson)).ToArray(), state, evidence,
                AttributedEvidence: attributed, PurchaseAttributedEvidence: purchased);
    }

    public async Task<InvoiceLineCogsCorrectionViewV1?> FindAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        var captured = Company();
        var approved = await db.InvoiceLineCogsRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.ProposalId == proposalId, cancellationToken);
        var prepared = approved is null ? await db.InvoiceLineCogsProposals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proposalId, cancellationToken) : null;
        RequireCompany(captured);
        return approved is not null ? Decode<InvoiceLineCogsCorrectionViewV1>(approved.ViewJson)
            : prepared is null ? null : Decode<InvoiceLineCogsCorrectionViewV1>(prepared.ViewJson);
    }

    public Task<InvoiceLineCogsCorrectionViewV1> PrepareAsync(FinalizedInvoiceLineSourceV1 source,
        PrepareInvoiceLineCogsCorrectionV1 request, Guid actorId, Func<CancellationToken, Task> freshFence,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) => Mutate(source, actorId, freshFence, metadata,
        async () =>
        {
            if (metadata.PermissionCode != AccountingJournalReversalPermissionNamesV1.Propose) throw Refuse(AccountingStoreCodes.CompanyContextMismatch);
            ValidateRequest(source, request);
            if (await db.InvoiceLineCogsProposals.AnyAsync(x => x.Id == request.ProposalId, cancellationToken)) throw Refuse(InvoiceLineCogsCorrectionCodesV1.VersionConflict);
            var current = await Latest(source, cancellationToken);
            if (request.ExpectedLineVersion != (current?.Version ?? 0))
                throw new AccountingVersionConflictException(request.ExpectedLineVersion, current?.Version ?? 0);
            await Period(request.PeriodId, request.AccountingDate, cancellationToken);
            var plan = await Account(request.CogsAccountId, cancellationToken);
            var (state, evidence) = await Evidence(source, current, cancellationToken);
            if (state == "Unreadable") throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            var attributed = await AttributedEvidence(source, cancellationToken);
            var purchased = await PurchaseAttributedEvidence(source, cancellationToken);
            if (attributed is not null || purchased is not null) await RequireMixedPosting(source, evidence, cancellationToken);
            var view = new InvoiceLineCogsCorrectionViewV1(request.ProposalId, source.CompanyId, source.InvoiceId,
                source.InvoiceLineId, source.FinalizedVersion, 1, request.ExpectedLineVersion, "Prepared", request.Classification,
                plan, request.PeriodId, request.TransactionDate, request.AccountingDate, request.ValueDate,
                actorId, clock.GetUtcNow(), null, null, request.Reason, evidence, null, null, attributed,
                PurchaseAttributedEvidence: purchased);
            db.InvoiceLineCogsProposals.Add(new() { CompanyId = source.CompanyId, Id = request.ProposalId,
                InvoiceId = source.InvoiceId, InvoiceLineId = source.InvoiceLineId, ExpectedLineVersion = request.ExpectedLineVersion,
                PreparedBy = actorId, PreparedAtUtc = view.PreparedAtUtc, SourceJson = Encode(source), ViewJson = Encode(view) });
            Enqueue(source, metadata, view, "prepared", current?.Version ?? 0, current);
            return view;
        }, cancellationToken);

    public Task<InvoiceLineCogsCorrectionViewV1> ApproveAsync(FinalizedInvoiceLineSourceV1 source, Guid proposalId,
        long expectedVersion, Guid actorId, Func<CancellationToken, Task> freshFence, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default) => Mutate(source, actorId, freshFence, metadata,
        async () =>
        {
            if (metadata.PermissionCode != AccountingJournalReversalPermissionNamesV1.Approve) throw Refuse(AccountingStoreCodes.CompanyContextMismatch);
            if (proposalId == Guid.Empty || expectedVersion < 1) throw Refuse(InvoiceLineCogsCorrectionCodesV1.RequestInvalid);
            var row = await db.InvoiceLineCogsProposals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proposalId, cancellationToken)
                ?? throw Refuse(InvoiceLineCogsCorrectionCodesV1.NotFound);
            var prepared = Decode<InvoiceLineCogsCorrectionViewV1>(row.ViewJson);
            if (row.InvoiceId != source.InvoiceId || row.InvoiceLineId != source.InvoiceLineId || row.SourceJson != Encode(source))
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceChanged);
            if (actorId == row.PreparedBy) throw Refuse(InvoiceLineCogsCorrectionCodesV1.SelfApproval);
            var priorApproval = await db.InvoiceLineCogsRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.ProposalId == proposalId, cancellationToken);
            if (expectedVersion != (priorApproval is null ? 1 : 2)) throw new AccountingVersionConflictException(expectedVersion, priorApproval is null ? 1 : 2);
            if (priorApproval is not null) throw Refuse(InvoiceLineCogsCorrectionCodesV1.NotPrepared);
            var current = await Latest(source, cancellationToken);
            if ((current?.Version ?? 0) != row.ExpectedLineVersion)
                throw new AccountingVersionConflictException(row.ExpectedLineVersion, current?.Version ?? 0);
            var period = await Period(prepared.PeriodId, prepared.AccountingDate, cancellationToken);
            if (Encode(await Account(prepared.AccountPlan.Account.AccountId, cancellationToken)) != Encode(prepared.AccountPlan))
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceChanged);
            var (state, evidence) = await Evidence(source, current, cancellationToken);
            if (state == "Unreadable") throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            if (Encode(evidence) != Encode(prepared.PostedEvidence)) throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceChanged);
            var attributed = await AttributedEvidence(source, cancellationToken);
            var purchased = await PurchaseAttributedEvidence(source, cancellationToken);
            if (Encode(attributed) != Encode(prepared.AttributedEvidence))
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceChanged);
            if (Encode(purchased) != Encode(prepared.PurchaseAttributedEvidence))
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceChanged);
            if (attributed is not null || purchased is not null) await RequireMixedPosting(source, evidence, cancellationToken);
            Guid? reverseId = null, replacementId = null;
            if (evidence is not null && evidence.CogsAccountId != prepared.AccountPlan.Account.AccountId)
            {
                var original = await Journal(evidence.JournalId, cancellationToken);
                var dates = new PostingDates(prepared.TransactionDate, prepared.AccountingDate, prepared.ValueDate, clock.GetUtcNow());
                var reversingCorrectionId = Guid.NewGuid();
                var replacementCorrectionId = Guid.NewGuid();
                var pair = InvoiceLineCogsCorrection.Create(original, decimal.Parse(evidence.Amount, CultureInfo.InvariantCulture), evidence.CogsAccountId,
                    evidence.CounterAccountId, prepared.AccountPlan.Account.AccountId, RestorePeriod(period), dates,
                    Guid.NewGuid(), Guid.NewGuid(), reversingCorrectionId, replacementCorrectionId);
                Add(pair.Reversing, prepared.PeriodId, reversingCorrectionId, metadata);
                Add(pair.Replacement, prepared.PeriodId, replacementCorrectionId, metadata);
                reverseId = pair.Reversing.Id;
                replacementId = pair.Replacement.Id;
                period.Version = checked(period.Version + 1);
                period.UpdatedAtUtc = clock.GetUtcNow();
                db.Entry(period).Property(x => x.UpdatedAtUtc).IsModified = true;
            }
            var now = clock.GetUtcNow();
            var attributedCorrections = await CorrectAttributed(attributed, prepared, period, metadata, cancellationToken);
            var purchaseCorrections = await CorrectPurchaseAttributed(purchased, prepared, period, metadata, cancellationToken);
            if (purchaseCorrections is not null) attributedCorrections = (attributedCorrections ?? []).Concat(purchaseCorrections).ToArray();
            var approved = prepared with { Version = 2, Status = "Approved", ApprovedBy = actorId, ApprovedAtUtc = now,
                ReversingCorrectionJournalId = reverseId, ReplacementCorrectionJournalId = replacementId,
                AttributedCorrections = attributedCorrections };
            var revision = new InvoiceLineCogsRevisionViewV1(checked(row.ExpectedLineVersion + 1), proposalId,
                prepared.Classification, prepared.AccountPlan, row.PreparedBy, actorId, now, prepared.PeriodId,
                prepared.TransactionDate, prepared.AccountingDate, reverseId, replacementId, prepared.Reason, attributedCorrections);
            db.InvoiceLineCogsRevisions.Add(new() { CompanyId = source.CompanyId, InvoiceId = source.InvoiceId,
                InvoiceLineId = source.InvoiceLineId, Version = revision.Version, ProposalId = proposalId,
                PreparedBy = row.PreparedBy, ApprovedBy = actorId, ApprovedAtUtc = now,
                ViewJson = Encode(approved), RevisionJson = Encode(revision) });
            Enqueue(source, metadata, approved, "approved", row.ExpectedLineVersion, prepared);
            return approved;
        }, cancellationToken);

    private async Task<InvoiceLineCogsCorrectionViewV1> Mutate(FinalizedInvoiceLineSourceV1 source, Guid actor,
        Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, Func<Task<InvoiceLineCogsCorrectionViewV1>> operation,
        CancellationToken cancellationToken)
    {
        var companyId = ValidateSource(source);
        if (actor == Guid.Empty || !Guid.TryParse(metadata.ActorId, out var parsed) || parsed != actor
            || string.IsNullOrWhiteSpace(metadata.PermissionCode) || string.IsNullOrWhiteSpace(metadata.CorrelationId)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)) throw Refuse(InvoiceLineCogsCorrectionCodesV1.RequestInvalid);
        // Reject before touching caller-owned state. A failed save is never swallowed and committed (including with MARS).
        if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || db.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts) throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable);
        var started = false;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            started = true;
            await fence(cancellationToken);
            // The same Serializable cancellation-key read used by initial COGS preparation/posting. A late line
            // reclassification cannot replace the journal while an official invoice cancellation is being reversed.
            if (await db.SourceInvoiceCancellations.AsNoTracking().AnyAsync(x => x.SourceDocumentId == source.FinalizationAdmissionId,
                    cancellationToken)) throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceChanged);
            var frozenSource = Encode(source);
            if (await db.InvoiceLineCogsProposals.AnyAsync(x => x.InvoiceId == source.InvoiceId
                && x.InvoiceLineId == source.InvoiceLineId && x.SourceJson != frozenSource, cancellationToken))
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceChanged);
            var result = await operation();
            await fence(cancellationToken);
            RequireCompany(companyId);
            if (metadata.Completion?.Invoke(result) is { } completion) db.Commands.Add(new()
            {
                CompanyId = companyId, CommandName = completion.CommandName, IdempotencyKeyHash = completion.IdempotencyKeyHash,
                ActorId = completion.ActorId, PayloadHash = completion.RequestHash, ResultJson = completion.ResultJson,
                CorrelationId = completion.OriginalCorrelationId, RecordedAtUtc = clock.GetUtcNow(),
            });
            await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await fence(cancellationToken);
            RequireCompany(companyId);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException) { throw Refuse(AccountingStoreCodes.ConcurrentChange); }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        { throw Refuse(AccountingStoreCodes.ConcurrentChange); }
        catch (DbUpdateException) { throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable); }
        catch (System.Data.Common.DbException) { throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable); }
        finally { if (started) { db.ChangeTracker.Clear(); auditWriter.DiscardPending(); } }
    }

    private IQueryable<InvoiceLineCogsRevisionRow> Revisions(FinalizedInvoiceLineSourceV1 source) => db.InvoiceLineCogsRevisions
        .Where(x => x.InvoiceId == source.InvoiceId && x.InvoiceLineId == source.InvoiceLineId);
    private async Task<InvoiceLineCogsRevisionViewV1?> Latest(FinalizedInvoiceLineSourceV1 source, CancellationToken token)
    {
        var row = await Revisions(source).AsNoTracking().OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
        return row is null ? null : Decode<InvoiceLineCogsRevisionViewV1>(row.RevisionJson);
    }

    private async Task<(string State, InvoiceLineCogsPostedEvidenceV1? Evidence)> Evidence(FinalizedInvoiceLineSourceV1 source,
        InvoiceLineCogsRevisionViewV1? current, CancellationToken token)
    {
        // Every query reads actual owner tables. No matching source facts is distinct from an unreadable/aggregate posting.
        // The initial producer must explicitly bind a journal to this invoice line; no quantity/price allocation is inferred.
        var rows = await db.Journals.AsNoTracking().Where(x => x.SourceId == source.InvoiceLineId || x.SourceId == source.InvoiceId
            || x.SourceId == source.FinalizationAdmissionId || x.SourceId == source.ShipmentId || x.SourceId == source.DispatchNoteId).ToListAsync(token);
        rows.RemoveAll(row => IsAnotherLineComponent(row, source));
        if (rows.Count == 0) return ("NoPostedCogs", null);
        var candidateId = current?.ReplacementCorrectionJournalId;
        // A classification-only revision or an unchanged account retains the last actual posted component.
        if (current is not null && candidateId is null)
        {
            var previousPosted = await Revisions(source).AsNoTracking().Where(x => x.Version <= current.Version)
                .OrderByDescending(x => x.Version).ToListAsync(token);
            candidateId = previousPosted.Select(x => Decode<InvoiceLineCogsRevisionViewV1>(x.RevisionJson))
                .FirstOrDefault(x => x.ReplacementCorrectionJournalId.HasValue)?.ReplacementCorrectionJournalId;
        }
        var candidate = candidateId is { } id ? rows.SingleOrDefault(x => x.Id == id)
            : rows.SingleOrDefault(x => x.SourceId == source.InvoiceId && x.PostingPurpose == InvoiceLineCogsPostingTraceV1.For(source.InvoiceLineId, source.FinalizedVersion) && x.Kind == (int)JournalKind.Original);
        if (candidate is null) return ("Unreadable", null);
        if (candidate.SourceId != source.InvoiceId || candidate.PostingPurpose != InvoiceLineCogsPostingTraceV1.For(source.InvoiceLineId, source.FinalizedVersion))
            return ("Unreadable", null);
        var lines = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == candidate.Id).OrderBy(x => x.LineNumber).ToListAsync(token);
        if (lines.Count != 2 || lines[0].LineNumber != 1 || lines[1].LineNumber != 2 || lines[0].Debit <= 0
            || lines[0].Credit != 0 || lines[1].Debit != 0 || lines[1].Credit != lines[0].Debit
            || lines[0].AccountId == lines[1].AccountId
            || await db.Journals.AnyAsync(x => x.LinkedJournalId == candidate.Id && x.Kind == (int)JournalKind.Reversal, token)) return ("Unreadable", null);
        var permitted = await Revisions(source).AsNoTracking().Select(x => x.RevisionJson).ToListAsync(token);
        var ownCorrections = permitted.Select(x => Decode<InvoiceLineCogsRevisionViewV1>(x)).SelectMany(x =>
            new[] { x.ReversingCorrectionJournalId, x.ReplacementCorrectionJournalId }).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        if (rows.Any(x => x.SourceId == source.InvoiceId && x.PostingPurpose == candidate.PostingPurpose && x.Id != candidate.Id
            && x.Kind != (int)JournalKind.Original && !ownCorrections.Contains(x.Id))) return ("Unreadable", null);
        return ("PostedCogs", new(source.CompanyId, source.InvoiceId, source.InvoiceLineId, source.FinalizedVersion,
            candidate.Id, 1, 2, lines[0].AccountId, lines[1].AccountId, candidate.CurrencyId,
            lines[0].Debit.ToString("0.############################", CultureInfo.InvariantCulture), source.Fingerprint));
    }

    private static bool IsAnotherLineComponent(JournalRow row, FinalizedInvoiceLineSourceV1 source)
    {
        if (row.SourceId != source.InvoiceId) return false;
        var pieces = row.PostingPurpose.Split(':');
        return pieces.Length == 3 && pieces[0] == InvoiceLineCogsPostingTraceV1.Purpose
            && Guid.TryParseExact(pieces[1], "D", out var lineId) && lineId != Guid.Empty && lineId != source.InvoiceLineId
            && long.TryParse(pieces[2], NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version > 0
            && row.PostingPurpose == InvoiceLineCogsPostingTraceV1.For(lineId, version);
    }

    private async Task<InvoiceLineCogsAccountPlanV1> Account(Guid accountId, CancellationToken token)
    {
        var captured = Company();
        var plan = await accountPlans.ReadActiveAsync(token);
        RequireCompany(captured);
        if (plan is null || plan.CompanyId != captured || plan.Status != ApprovedAccountingSourceCatalogStatusV1.Active
            || plan.Package.Mirror.CompanyId != captured) throw Refuse(InvoiceLineCogsCorrectionCodesV1.AccountPlanUnavailable);
        var account = plan.Package.Mirror.Lookup(new(captured, accountId))
            ?? throw Refuse(InvoiceLineCogsCorrectionCodesV1.AccountNotInActivePlan);
        return new(plan.PackageId, plan.Version, plan.CatalogVersion, plan.AccountPlanSourceVersion, plan.AccountsSourceSha256,
            new(account.Account.AccountId, account.Code, account.Name));
    }

    private async Task<PeriodRow> Period(Guid id, DateOnly date, CancellationToken token)
    {
        var period = await db.Periods.SingleOrDefaultAsync(x => x.Id == id, token);
        if (period is null || period.Status != (int)AccountingPeriodStatus.Open || date < period.StartsOn || date > period.EndsOn)
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.OpenPeriodRequired);
        return period;
    }
    private static AccountingPeriod RestorePeriod(PeriodRow p) => AccountingPeriod.Restore(p.Id, p.CompanyId, p.StartsOn,
        p.EndsOn, p.PreparedByUserId, p.PreparedAtUtc, p.ApprovedByUserId, p.ApprovedAtUtc, p.ClosedByUserId, p.ClosedAtUtc);
    private async Task<Journal> Journal(Guid id, CancellationToken token)
    {
        var row = await db.Journals.AsNoTracking().SingleAsync(x => x.Id == id, token);
        var lines = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == id).OrderBy(x => x.LineNumber).ToListAsync(token);
        return GoymenERP.Modules.Accounting.Domain.Journals.Journal.Restore(row.Id, (JournalKind)row.Kind, new(row.CompanyId, row.SourceId, row.PostingPurpose),
            row.PostingRuleVersion, row.CurrencyId, new(row.TransactionDate, row.AccountingDate, row.ValueDate, row.RecordedAtUtc),
            lines.Select(x => new JournalLine(new(x.CompanyId, x.AccountId), x.Debit, x.Credit)), row.LinkedJournalId);
    }
    private void Add(Journal journal, Guid periodId, Guid correctionId, AccountingCommandMetadata metadata)
    {
        db.Journals.Add(new() { CompanyId = journal.CompanyId, Id = journal.Id, Kind = (int)journal.Kind,
            SourceId = journal.Source.SourceId, PostingPurpose = journal.Source.PostingPurpose, PostingRuleVersion = journal.PostingRuleVersion,
            CurrencyId = journal.CurrencyId, TransactionDate = journal.Dates.TransactionDate, AccountingDate = journal.Dates.AccountingDate,
            ValueDate = journal.Dates.ValueDate, RecordedAtUtc = journal.Dates.RecordedAtUtc, LinkedJournalId = journal.LinkedJournalId,
            CorrectionId = correctionId, PeriodId = periodId, StoredAtUtc = clock.GetUtcNow(), StoredBy = metadata.ActorId, CorrelationId = metadata.CorrelationId });
        db.JournalLines.AddRange(journal.Lines.Select((line, index) => new JournalLineRow { CompanyId = journal.CompanyId,
            JournalId = journal.Id, LineNumber = index + 1, AccountId = line.Account.AccountId, Debit = line.Debit, Credit = line.Credit }));
    }
    private void Enqueue(FinalizedInvoiceLineSourceV1 source, AccountingCommandMetadata metadata,
        InvoiceLineCogsCorrectionViewV1 view, string action, long before, object? previous) => audit.Enqueue(new AuditEventDraft(source.CompanyId,
        "accounting.invoice-line-cogs-correction." + action + ".v1", Entity, view.ProposalId.ToString("D"), action,
        action == "approved" ? 1 : null, action == "approved" ? 2 : 1, AuditActorTypes.User, metadata.ActorId, metadata.PermissionCode,
        metadata.RoleCodes, previous is null ? "{}" : Encode(previous), Encode(view), view.Reason,
        Encode(new { view.PreparedBy, view.ApprovedBy, source.InvoiceId, source.InvoiceLineId, source.FinalizationAdmissionId,
            source.Fingerprint, LineVersionBefore = before, LineVersionAfter = action == "approved" ? before + 1 : before }), AuditAiInvolvement.None, null,
        metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
    private Guid ValidateSource(FinalizedInvoiceLineSourceV1 source)
    {
        var captured = Company();
        if (source.CompanyId != captured || source.InvoiceId == Guid.Empty || source.InvoiceLineId == Guid.Empty
            || source.FinalizationAdmissionId == Guid.Empty || source.FinalizedVersion < 1
            || source.Fingerprint is not { Length: 64 } || !source.Fingerprint.All(char.IsAsciiHexDigit)
            || source.ProductId == Guid.Empty || source.ShipmentId == Guid.Empty || source.DispatchNoteId == Guid.Empty
            || source.DispatchNoteLineNo < 1 || source.InvoiceLineNo < 1 || source.InvoiceDate == default
            || source.Ettn == Guid.Empty || string.IsNullOrWhiteSpace(source.OfficialInvoiceNumber)
            || string.IsNullOrWhiteSpace(source.CommercialProofJson))
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable);
        return captured;
    }
    private static void ValidateRequest(FinalizedInvoiceLineSourceV1 source, PrepareInvoiceLineCogsCorrectionV1 request)
    {
        if (request.ProposalId == Guid.Empty || request.ExpectedLineVersion < 0 || request.CogsAccountId == Guid.Empty
            || request.PeriodId == Guid.Empty || !Enum.IsDefined(request.Classification) || request.TransactionDate == default
            || request.AccountingDate == default || request.ValueDate == DateOnly.MinValue || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason != request.Reason.Trim() || request.Reason.Length > 1000) throw Refuse(InvoiceLineCogsCorrectionCodesV1.RequestInvalid);
        if (request.ExpectedFinalizedVersion != source.FinalizedVersion) throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceChanged);
    }
    private Guid Company() => company.IsResolved && company.CompanyId != Guid.Empty ? company.CompanyId
        : throw Refuse(AccountingStoreCodes.CompanyContextMismatch);
    private void RequireCompany(Guid id) { if (Company() != id) throw Refuse(AccountingStoreCodes.CompanyContextMismatch); }
    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Json) ?? throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable);
    private static AccountingStoreException Refuse(string code) => new(code, "Fatura satırı sınıfı ve SMM hesabı düzeltmesi tamamlanamadı.");
}
