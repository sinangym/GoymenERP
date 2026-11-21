using System.Globalization;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingInventoryCostStoreV1
{
    private async Task<AccountingInventoryCostSourceV1> NormalizeSuccessions(AccountingInventoryCostSourceV1 source, CancellationToken token)
    {
        if (source.Kind != AccountingInventoryCostKindV1.SalesInvoicedShipment) return source;
        var components = new List<AccountingInventoryCostComponentV1>();
        foreach (var component in source.Components)
        {
            if (component.SalesPhysicalClaims is not { Count: > 0 } physical
                || source.ObsoleteComponents?.Any(x => x.ComponentKey == component.ComponentKey) == true)
            { components.Add(component); continue; }
            var owners = new List<InvoiceCogsClaimHistoryV1.Entry>();
            foreach (var item in physical)
            {
                var history = await InvoiceCogsClaimHistoryV1.ReadAsync(db, source.CompanyId, item.MovementId,
                    item.MovementLineNumber, () => RequireCompany(source.CompanyId), token);
                if (history.LastOrDefault() is { } owner) owners.Add(owner);
            }
            if (owners.Count == 0) { components.Add(component); continue; }
            // One whole approved dispatch line remains indivisible. Never invent a price/amount split between owners.
            if (owners.Count != physical.Count || owners.Select(x => (x.Claim.SalesEventId, x.Claim.InvoiceLineId)).Distinct().Count() != 1
                || owners.Any(x => x.Claim.InvoiceId == source.InvoiceId || x.Claim.AdmissionId == source.AdmissionId)) throw SuccessionPending();
            var old = owners[0].Claim;
            var row = await db.InventoryCostProposals.AsNoTracking().Where(x => x.EventId == old.SalesEventId
                && x.ComponentKey == AccountingInventoryCostTraceV1.SalesComponent(old.InvoiceLineId))
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
            RequireCompany(source.CompanyId);
            if (row is null) throw SuccessionPending();
            var previous = await ReadStored(row, token);
            var oldPhysical = previous.Component.SalesPhysicalClaims;
            if (previous.CompanyId != source.CompanyId || previous.InvoiceId != old.InvoiceId || previous.AdmissionId != old.AdmissionId
                || previous.InvoiceVersion != old.FinalizedVersion || previous.Component.ProductId != component.ProductId
                || oldPhysical is null || oldPhysical.Count != physical.Count || physical.Any(x => !oldPhysical.Contains(x))) throw SuccessionPending();
            var cancelled = await Cancellation(previous, token) ?? throw SuccessionPending();
            var answer = await CancellationEvidence(previous, token);
            if (answer.Pending.Count != 0) throw SuccessionPending();
            var reader = new InvoiceLineCogsCorrectionStoreV1(db, company, plans, audit, auditWriter, clock, postedManufacturing, postedPurchasing);
            var evidence = await reader.ReadCancellationComponentsAsync(previous, token);
            var reversals = await db.InventoryCostCancellationClaims.AsNoTracking().Where(x => x.InvoiceId == previous.InvoiceId
                && x.InvoiceLineId == previous.Component.InvoiceLineId && x.FinalizedVersion == previous.InvoiceVersion).ToListAsync(token);
            RequireCompany(source.CompanyId);
            var restored = previous.Component.UnpostedTlAmount ?? (previous.Component.HasPostedComponents
                ? AccountingInventoryCostAmountsV1.FromDecimal(0) : previous.Component.SignedTlAmount);
            var knownBase = restored;
            foreach (var part in evidence.Components.Where(x => x.Origin != "Known"))
                restored = AccountingInventoryCostAmountsV1.Add(restored, AccountingInventoryCostAmountsV1.FromDecimal(
                    decimal.Parse(part.SignedAmount, CultureInfo.InvariantCulture)));
            if (!AccountingInventoryCostAmountsV1.TryRead(restored, out var amount, out _) || amount.Sign < 0) throw SuccessionPending();
            var captured = (previous.Component.ClaimSuccession?.CapturedCostFingerprints ?? [])
                .Concat(evidence.Components.Where(x => x.Origin != "Known").Select(x => x.EconomicKey))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var header = await db.InventoryCostSources.AsNoTracking().SingleAsync(x => x.EventId == previous.EventId, token);
            RequireCompany(source.CompanyId);
            var proof = new InventoryCostClaimSuccessionV1(previous.EventId, previous.InvoiceId, previous.Component.InvoiceLineId!.Value,
                previous.AdmissionId, previous.InvoiceVersion, previous.ProposalId, previous.Version,
                AccountingInventoryCostSourceValidationV1.Sha256(header.SourceJson), cancelled.CancellationId, evidence.Revision,
                physical.Select(x => { var owner = owners.Single(o => o.Claim.MovementId == x.MovementId && o.Claim.MovementLineNumber == x.MovementLineNumber);
                    return new InventoryCostClaimGenerationV1(x, owner.Generation, owner.Claim.SourceHash); }).ToArray(),
                reversals.Select(x => Decode<InventoryCostCancellationComponentV1>(x.ViewJson)).OrderBy(x => x.EconomicKey, StringComparer.Ordinal).ToArray(),
                captured, component.SignedTlAmount, knownBase, restored);
            // The original owner proof remains intact. Only Accounting's verified cancellation normalizes the current economic amount.
            components.Add(component with { SignedTlAmount = restored, AlreadyPostedComponents = null,
                AlreadyPostedPurchaseComponents = null, UnpostedTlAmount = null, ClaimSuccession = proof });
        }
        return source with { Components = components };
    }

    private async Task<InventoryCostClaimSuccessionV1?> SupersedingProof(AccountingInventoryCostProposalV1 view, CancellationToken token)
    {
        if (view.Component.SalesPhysicalClaims is not { Count: > 0 } physical) return null;
        InventoryCostClaimSuccessionV1? proof = null; var count = 0;
        foreach (var item in physical)
        {
            var history = await InvoiceCogsClaimHistoryV1.ReadAsync(db, view.CompanyId, item.MovementId, item.MovementLineNumber,
                () => RequireCompany(view.CompanyId), token);
            var next = history.SingleOrDefault(x => x.Succession?.PreviousEventId == view.EventId
                && x.Succession.PreviousInvoiceLineId == view.Component.InvoiceLineId)?.Succession;
            if (next is null) continue;
            if (proof is not null && Encode(proof) != Encode(next)) throw SuccessionPending();
            proof = next; count++;
        }
        if (count != 0 && count != physical.Count) throw SuccessionPending();
        return proof;
    }

    private static AccountingInvoiceJournalRetryException SuccessionPending() => new("inventory_cost_claim_release_pending",
        "Önceki faturanın gerçek maliyet payı iptal ve ters kayıt kanıtıyla serbest bırakılmayı bekliyor.");
}
