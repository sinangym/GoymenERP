using System.Globalization;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class InvoiceLineCogsCorrectionStoreV1
{
    private async Task<(IReadOnlyList<PostedManufacturingCostShareV1> Manufacturing, IReadOnlyList<PostedPurchaseCostShareV1> Purchase)>
        ReadAttributedPhysical(CogsEvidenceIdentityV1 source, InvoiceCogsEconomicClaimRow claim,
            AccountingInventoryCostComponentV1 component, AccountingSalesPhysicalCostClaimV1 physical, CancellationToken token)
    {
        var proof = Encode(new { source.CompanyId, EventId = claim.SalesEventId, source.InvoiceId,
            InvoiceVersion = source.FinalizedVersion, AdmissionId = source.FinalizationAdmissionId,
            component.InvoiceLineId, Physical = physical, component.SourceProofSha256 });
        if (claim.ViewJson != proof || claim.SourceHash != AccountingInventoryCostSourceValidationV1.Sha256(proof))
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        Task Fence(CancellationToken _) { RequireCompany(source.CompanyId); return Task.CompletedTask; }
        IReadOnlyList<PostedManufacturingCostShareV1> manufacturing = [];
        IReadOnlyList<PostedPurchaseCostShareV1> purchase = [];
        if (claim.Origin == 1)
        {
            if (postedManufacturing is null) throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            var deltas = await postedManufacturing.ReadPostedDeltasAsync(source.CompanyId, source.ShipmentId, physical.MovementId,
                physical.MovementLineNumber, source.ProductId, physical.QuantityKg, Fence, token);
            RequireCompany(source.CompanyId);
            if (deltas is null) return (manufacturing, purchase);
            // Cancellation can answer an actual posted delta even if the separate initial known proposal
            // was never posted. It never creates that initial journal or claims its amount as accounted.
            if (!source.CancellationRead) await RequireCompletedKnownSource(source, claim.SalesEventId, component, token);
            // The original known source already proves physical coverage. These are only independently posted
            // changes; they cannot be reused by the unavailable-original-cost admission path.
            return (deltas.Shares, purchase);
        }
        if (component.AlreadyPostedComponents?.Any(x => x.Shipment.MovementId == physical.MovementId
                && x.Shipment.MovementLineNumber == physical.MovementLineNumber) == true)
        {
            if (postedManufacturing is null) throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            var actual = await postedManufacturing.ReadProvenSharesAsync(source.CompanyId, source.ShipmentId, physical.MovementId,
                physical.MovementLineNumber, source.ProductId, physical.QuantityKg, Fence, token);
            RequireCompany(source.CompanyId);
            manufacturing = actual?.Shares ?? throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        }
        if (component.AlreadyPostedPurchaseComponents?.Any(x => x.MovementId == physical.MovementId
                && x.MovementLineNumber == physical.MovementLineNumber) == true)
        {
            if (postedPurchasing is null) throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            var actual = await postedPurchasing.ReadProvenSharesAsync(source.CompanyId, source.ShipmentId, physical.MovementId,
                physical.MovementLineNumber, source.ProductId, physical.QuantityKg, Fence, token);
            RequireCompany(source.CompanyId);
            if (actual.HasPendingCandidate) throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            purchase = actual.Shares;
        }
        if (!PostedCostPhysicalCoverageV1.IsComplete(manufacturing, purchase))
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        return (manufacturing, purchase);
    }

    private async Task<IReadOnlyList<InvoiceLineCogsPurchaseAttributedEvidenceV1>?> PurchaseAttributedEvidence(
        CogsEvidenceIdentityV1 source, CancellationToken token)
    {
        var claims = await db.InvoiceCogsEconomicClaims.AsNoTracking().Where(x => x.InvoiceId == source.InvoiceId
            && x.InvoiceLineId == source.InvoiceLineId && x.FinalizedVersion == source.FinalizedVersion && x.Origin == 2).ToListAsync(token);
        RequireCompany(source.CompanyId);
        if (claims.Count == 0) return null;
        var revisions = await Revisions(source).AsNoTracking().OrderBy(x => x.Version).Select(x => x.RevisionJson).ToListAsync(token);
        RequireCompany(source.CompanyId);
        var prior = revisions.Select(x => Decode<InvoiceLineCogsRevisionViewV1>(x)).ToArray();
        var result = new List<InvoiceLineCogsPurchaseAttributedEvidenceV1>();
        foreach (var claim in claims)
        {
            var header = await db.InventoryCostSources.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == claim.SalesEventId, token)
                ?? throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            RequireCompany(source.CompanyId);
            var recorded = Decode<AccountingInventoryCostSourceV1>(header.SourceJson);
            var component = recorded.Components.SingleOrDefault(x => x.InvoiceLineId == source.InvoiceLineId);
            var physical = component?.SalesPhysicalClaims?.SingleOrDefault(x => x.MovementId == claim.MovementId
                && x.MovementLineNumber == claim.MovementLineNumber);
            if (recorded.CompanyId != source.CompanyId || recorded.InvoiceId != source.InvoiceId || recorded.AdmissionId != source.FinalizationAdmissionId
                || recorded.InvoiceVersion != source.FinalizedVersion || recorded.EventId != claim.SalesEventId
                || claim.AdmissionId != source.FinalizationAdmissionId || claim.ShipmentId != source.ShipmentId
                || physical is null || physical.ProductId != source.ProductId || physical.ShipmentId != source.ShipmentId
                || component?.HasPostedComponents != true) throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            var actual = await ReadAttributedPhysical(source, claim, component, physical, token);
            foreach (var share in actual.Purchase)
            {
                var approval = share.Approval;
                var amount = decimal.Parse(share.SignedAmount, CultureInfo.InvariantCulture);
                var corrected = prior.SelectMany(x => x.AttributedCorrections ?? []).LastOrDefault(x => x.AttributionFingerprint == share.Fingerprint);
                var currentId = corrected?.ReplacementCorrectionJournalId ?? approval.JournalId;
                if (currentId is null)
                {
                    if (approval.Status != "RoundedNoCost" || amount != 0 || corrected is not null)
                        throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
                    result.Add(new(share, null, approval.CogsAccountId, approval.CounterAccountId, "0"));
                    continue;
                }
                var journal = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == currentId, token);
                RequireCompany(source.CompanyId);
                var lines = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == currentId).OrderBy(x => x.LineNumber).ToListAsync(token);
                RequireCompany(source.CompanyId);
                var total = corrected is null ? decimal.Parse(approval.SignedAmount, CultureInfo.InvariantCulture) : amount;
                if (journal is null || journal.CompanyId != source.CompanyId || journal.SourceId != approval.ProposalId
                    || journal.PostingPurpose != AccountingInventoryCostTraceV1.PurchaseDifferencePurpose || journal.CurrencyId != approval.CurrencyId
                    || journal.Kind != (corrected is null ? (int)JournalKind.Original : (int)JournalKind.Correction)
                    || amount < 0 || total < amount || lines.Count != 2 || lines[0].LineNumber != 1 || lines[1].LineNumber != 2
                    || lines[0].Debit != total || lines[0].Credit != 0 || lines[1].Credit != total || lines[1].Debit != 0
                    || lines[1].AccountId != approval.CounterAccountId || corrected is null && lines[0].AccountId != approval.CogsAccountId)
                    throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
                if (await db.Journals.AnyAsync(x => x.LinkedJournalId == currentId && x.Kind == (int)JournalKind.Reversal, token))
                    throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
                RequireCompany(source.CompanyId);
                if (await db.JournalReversalProposals.AnyAsync(x => x.SourceJournalId == currentId && x.Status == 1, token))
                    throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
                RequireCompany(source.CompanyId);
                result.Add(new(share, currentId, lines[0].AccountId, lines[1].AccountId,
                    total.ToString("0.############################", CultureInfo.InvariantCulture)));
            }
        }
        if (result.Select(x => x.Source.Fingerprint).Distinct().Count() != result.Count)
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        return result.Count == 0 ? null : result.OrderBy(x => x.Source.Approval.SourceEventId).ThenBy(x => x.Source.Approval.InvoiceLineId)
            .ThenBy(x => x.Source.MovementId).ThenBy(x => x.Source.MovementLineNumber).ToArray();
    }

    private async Task<IReadOnlyList<InvoiceLineCogsAttributedCorrectionV1>?> CorrectPurchaseAttributed(
        IReadOnlyList<InvoiceLineCogsPurchaseAttributedEvidenceV1>? evidence, InvoiceLineCogsCorrectionViewV1 prepared,
        PeriodRow period, AccountingCommandMetadata metadata, CancellationToken token)
    {
        if (evidence is null) return null;
        var corrections = new List<InvoiceLineCogsAttributedCorrectionV1>();
        foreach (var item in evidence)
        {
            var share = item.Source; var approval = share.Approval;
            var amount = decimal.Parse(share.SignedAmount, CultureInfo.InvariantCulture);
            if (amount == 0 || item.CurrentCogsAccountId == prepared.AccountPlan.Account.AccountId) continue;
            var journal = await Journal(item.CurrentJournalId ?? throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid), token);
            RequireCompany(prepared.CompanyId);
            var component = new PurchaseAttributedPostedJournalComponent(prepared.CompanyId, approval.SourceEventId, approval.InvoiceLineId,
                approval.ProposalId, share.MovementId, share.MovementLineNumber, journal.Id,
                AccountingInventoryCostTraceV1.PurchaseDifferencePurpose, approval.CurrencyId,
                decimal.Parse(item.CurrentJournalTotal, CultureInfo.InvariantCulture), amount, item.CurrentCogsAccountId,
                item.CounterAccountId, share.Fingerprint);
            var reverseCorrection = Guid.NewGuid(); var replacementCorrection = Guid.NewGuid();
            var pair = InvoiceLineCogsCorrection.CreatePurchaseAttributed(journal, component, prepared.AccountPlan.Account.AccountId,
                RestorePeriod(period), new(prepared.TransactionDate, prepared.AccountingDate, prepared.ValueDate, clock.GetUtcNow()),
                Guid.NewGuid(), Guid.NewGuid(), reverseCorrection, replacementCorrection);
            Add(pair.Reversing, prepared.PeriodId, reverseCorrection, metadata);
            Add(pair.Replacement, prepared.PeriodId, replacementCorrection, metadata);
            corrections.Add(new(share.Fingerprint, journal.Id, pair.Reversing.Id, pair.Replacement.Id));
        }
        if (corrections.Count > 0)
        {
            period.Version = checked(period.Version + 1); period.UpdatedAtUtc = clock.GetUtcNow();
            db.Entry(period).Property(x => x.UpdatedAtUtc).IsModified = true;
        }
        return corrections;
    }
}
