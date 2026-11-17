using System.Globalization;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class InvoiceLineCogsCorrectionStoreV1
{
    private async Task<IReadOnlyList<InvoiceLineCogsAttributedEvidenceV1>?> AttributedEvidence(FinalizedInvoiceLineSourceV1 source,
        CancellationToken token)
    {
        var claims = await db.InvoiceCogsEconomicClaims.AsNoTracking().Where(x => x.InvoiceId == source.InvoiceId
            && x.InvoiceLineId == source.InvoiceLineId && x.FinalizedVersion == source.FinalizedVersion).ToListAsync(token);
        RequireCompany(source.CompanyId);
        if (claims.Count == 0 || claims.All(x => x.Origin == 1)) return null;
        if (claims.Any(x => x.Origin is not (1 or 2) || x.AdmissionId != source.FinalizationAdmissionId || x.ShipmentId != source.ShipmentId)
            || postedManufacturing is null) throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        var revisions = await Revisions(source).AsNoTracking().OrderBy(x => x.Version).Select(x => x.RevisionJson).ToListAsync(token);
        RequireCompany(source.CompanyId);
        var prior = revisions.Select(x => Decode<InvoiceLineCogsRevisionViewV1>(x)).ToArray();
        var result = new List<InvoiceLineCogsAttributedEvidenceV1>();
        foreach (var claim in claims.Where(x => x.Origin == 2))
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
                || physical is null || physical.ProductId != source.ProductId || physical.ShipmentId != source.ShipmentId
                || component?.AlreadyPostedComponents is not { Count: > 0 }
                || claim.SourceHash != AccountingInventoryCostSourceValidationV1.Sha256(claim.ViewJson))
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            var actual = await postedManufacturing.ReadAsync(source.CompanyId, source.ShipmentId, physical.MovementId,
                physical.MovementLineNumber, source.ProductId, physical.QuantityKg,
                _ => { RequireCompany(source.CompanyId); return Task.CompletedTask; }, token);
            RequireCompany(source.CompanyId);
            if (actual is null) throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            foreach (var share in actual.Shares)
            {
                var signed = decimal.Parse(share.SignedAmount, CultureInfo.InvariantCulture);
                var corrected = prior.SelectMany(x => x.AttributedCorrections ?? []).LastOrDefault(x => x.AttributionFingerprint == share.Fingerprint);
                var currentId = corrected?.ReplacementCorrectionJournalId ?? share.JournalId;
                var journal = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == currentId, token);
                RequireCompany(source.CompanyId);
                var lines = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == currentId).OrderBy(x => x.LineNumber).ToListAsync(token);
                RequireCompany(source.CompanyId);
                var total = corrected is null ? decimal.Parse(share.JournalTotal, CultureInfo.InvariantCulture) : Math.Abs(signed);
                if (journal is null || journal.CompanyId != source.CompanyId || journal.SourceId != share.SourceEventId
                    || journal.PostingPurpose != ManufacturingCostDifferenceInboxV1.PostingPurpose || journal.CurrencyId != share.CurrencyId
                    || lines.Count != 2 || lines[0].LineNumber != 1 || lines[1].LineNumber != 2 || lines[0].Debit != total
                    || lines[0].Credit != 0 || lines[1].Credit != total || lines[1].Debit != 0
                    || journal.Kind != (corrected is null ? (int)JournalKind.Original : (int)JournalKind.Correction))
                    throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
                var cogs = signed >= 0 ? lines[0] : lines[1]; var counter = signed >= 0 ? lines[1] : lines[0];
                // A zero share needs no correction journal. Its source sign, accounts and amount are retained as evidence.
                if (signed == 0) { cogs = lines.Single(x => x.AccountId == share.CogsAccountId); counter = lines.Single(x => x.AccountId == share.CounterAccountId); }
                if (counter.AccountId != share.CounterAccountId || corrected is null && cogs.AccountId != share.CogsAccountId
                    || await db.Journals.AnyAsync(x => x.LinkedJournalId == currentId && x.Kind == (int)JournalKind.Reversal, token))
                    throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
                RequireCompany(source.CompanyId);
                if (await db.JournalReversalProposals.AnyAsync(x => x.SourceJournalId == currentId && x.Status == 1, token))
                    throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
                RequireCompany(source.CompanyId);
                result.Add(new(share, currentId, cogs.AccountId, counter.AccountId,
                    total.ToString("0.############################", CultureInfo.InvariantCulture)));
            }
        }
        if (result.Select(x => x.Source.Fingerprint).Distinct().Count() != result.Count)
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        return result.OrderBy(x => x.Source.SourceEventId).ThenBy(x => x.Source.OutputId)
            .ThenBy(x => x.Source.Shipment.MovementId).ThenBy(x => x.Source.Shipment.MovementLineNumber).ToArray();
    }

    /// <summary>
    /// A normal two-line journal and attributed aggregate shares coexist only when the actual initial proposal
    /// proves disjoint physical origins. This does not weaken the ordinary journal's two-line proof.
    /// </summary>
    private async Task RequireMixedPosting(FinalizedInvoiceLineSourceV1 source, InvoiceLineCogsPostedEvidenceV1? evidence,
        CancellationToken token)
    {
        if (evidence is null) return;
        var claims = await db.InvoiceCogsEconomicClaims.AsNoTracking().Where(x => x.InvoiceId == source.InvoiceId
            && x.InvoiceLineId == source.InvoiceLineId && x.FinalizedVersion == source.FinalizedVersion).ToListAsync(token);
        RequireCompany(source.CompanyId);
        if (!claims.Any(x => x.Origin == 1) || !claims.Any(x => x.Origin == 2)
            || claims.Any(x => x.Origin is not (1 or 2) || x.AdmissionId != source.FinalizationAdmissionId || x.ShipmentId != source.ShipmentId)
            || claims.Select(x => x.SalesEventId).Distinct().Count() != 1)
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        var eventId = claims[0].SalesEventId;
        var header = await db.InventoryCostSources.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == eventId, token)
            ?? throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        RequireCompany(source.CompanyId);
        var owner = Decode<AccountingInventoryCostSourceV1>(header.SourceJson);
        var component = owner.Components.SingleOrDefault(x => x.InvoiceLineId == source.InvoiceLineId);
        if (owner.CompanyId != source.CompanyId || owner.InvoiceId != source.InvoiceId || owner.EventId != eventId
            || owner.AdmissionId != source.FinalizationAdmissionId || owner.InvoiceVersion != source.FinalizedVersion
            || component is not { UnpostedTlAmount: { } unposted, AlreadyPostedComponents: { Count: > 0 } posted,
                SalesPhysicalClaims: { } physical } || component.ProductId != source.ProductId || physical.Count != claims.Count
            || claims.Any(c => !physical.Any(p => p.MovementId == c.MovementId && p.MovementLineNumber == c.MovementLineNumber
                && p.ShipmentId == c.ShipmentId && p.ProductId == source.ProductId)
                || (c.Origin == 2) != posted.Any(p => p.Shipment.MovementId == c.MovementId && p.Shipment.MovementLineNumber == c.MovementLineNumber)))
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        foreach (var claim in claims)
        {
            var actual = physical.Single(p => p.MovementId == claim.MovementId && p.MovementLineNumber == claim.MovementLineNumber);
            var proof = Encode(new { source.CompanyId, EventId = owner.EventId, source.InvoiceId,
                InvoiceVersion = source.FinalizedVersion, AdmissionId = source.FinalizationAdmissionId,
                component.InvoiceLineId, Physical = actual, component.SourceProofSha256 });
            if (claim.ViewJson != proof || claim.SourceHash != AccountingInventoryCostSourceValidationV1.Sha256(proof))
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        }
        var rows = await db.InventoryCostProposals.AsNoTracking().Where(x => x.EventId == eventId
            && x.ComponentKey == component.ComponentKey && x.Status == 3).ToListAsync(token);
        RequireCompany(source.CompanyId);
        if (rows.Count != 1) throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        var accepted = Decode<AccountingInventoryCostProposalV1>(rows[0].ViewJson);
        if (accepted.CompanyId != source.CompanyId || accepted.EventId != eventId || accepted.InvoiceId != source.InvoiceId
            || accepted.AdmissionId != source.FinalizationAdmissionId || accepted.InvoiceVersion != source.FinalizedVersion
            || accepted.Status != "Posted" || Encode(accepted.Component) != Encode(component) || accepted.JournalId != rows[0].JournalId
            || accepted.Preparation is not { Rounding: { } rounding } preparation || rounding.ExactSignedAmount != unposted
            || !AccountingInventoryCostRoundingV1Rules.TryResolve(component, preparation.CurrencyId, preparation.MoneyProfileId,
                preparation.MoneyProfileVersion, preparation.MoneyScale, out _, out var expected) || expected != rounding
            || preparation.SignedAmount != evidence.Amount || preparation.CurrencyId != evidence.CurrencyId)
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        var original = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == accepted.JournalId, token);
        RequireCompany(source.CompanyId);
        if (original is null || original.Kind != (int)JournalKind.Original || original.SourceId != source.InvoiceId
            || original.PostingPurpose != InvoiceLineCogsPostingTraceV1.For(source.InvoiceLineId, source.FinalizedVersion))
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
    }

    private async Task<IReadOnlyList<InvoiceLineCogsAttributedCorrectionV1>?> CorrectAttributed(
        IReadOnlyList<InvoiceLineCogsAttributedEvidenceV1>? evidence, InvoiceLineCogsCorrectionViewV1 prepared,
        PeriodRow period, AccountingCommandMetadata metadata, CancellationToken token)
    {
        if (evidence is null) return null;
        var corrections = new List<InvoiceLineCogsAttributedCorrectionV1>();
        foreach (var item in evidence)
        {
            var source = item.Source;
            var signed = decimal.Parse(source.SignedAmount, CultureInfo.InvariantCulture);
            if (signed == 0 || item.CurrentCogsAccountId == prepared.AccountPlan.Account.AccountId) continue;
            var journal = await Journal(item.CurrentJournalId, token);
            RequireCompany(prepared.CompanyId);
            var component = new AttributedPostedJournalComponent(prepared.CompanyId, source.SourceEventId, source.OutputId,
                source.Shipment.MovementId, source.Shipment.MovementLineNumber, item.CurrentJournalId, source.SourceEventId,
                ManufacturingCostDifferenceInboxV1.PostingPurpose, source.CurrencyId,
                decimal.Parse(item.CurrentJournalTotal, CultureInfo.InvariantCulture), signed, item.CurrentCogsAccountId,
                item.CounterAccountId, source.Fingerprint);
            var reverseCorrection = Guid.NewGuid(); var replacementCorrection = Guid.NewGuid();
            var pair = InvoiceLineCogsCorrection.CreateAttributed(journal, component, prepared.AccountPlan.Account.AccountId,
                RestorePeriod(period), new(prepared.TransactionDate, prepared.AccountingDate, prepared.ValueDate, clock.GetUtcNow()),
                Guid.NewGuid(), Guid.NewGuid(), reverseCorrection, replacementCorrection);
            Add(pair.Reversing, prepared.PeriodId, reverseCorrection, metadata);
            Add(pair.Replacement, prepared.PeriodId, replacementCorrection, metadata);
            corrections.Add(new(source.Fingerprint, item.CurrentJournalId, pair.Reversing.Id, pair.Replacement.Id));
        }
        if (corrections.Count > 0)
        {
            period.Version = checked(period.Version + 1); period.UpdatedAtUtc = clock.GetUtcNow();
            db.Entry(period).Property(x => x.UpdatedAtUtc).IsModified = true;
        }
        return corrections;
    }
}
