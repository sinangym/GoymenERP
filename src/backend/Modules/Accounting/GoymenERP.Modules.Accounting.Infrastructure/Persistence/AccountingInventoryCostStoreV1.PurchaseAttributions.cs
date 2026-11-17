using System.Globalization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingInventoryCostStoreV1
{
    private async Task<bool?> RecordPurchaseCandidates(WorkInboxEventV1 envelope, AccountingInventoryCostSourceV1 source,
        AccountingInventoryCostComponentV1 component, CancellationToken token)
    {
        if (!AccountingInventoryCostTraceV1.IsPurchaseFirstLineComponent(component.ComponentKey)) return null;
        if (source.Kind != AccountingInventoryCostKindV1.PurchaseInvoiceDifference || purchaseLineage is null)
            throw Refuse("purchase_cost_attribution_owner_unavailable");
        var lineage = await purchaseLineage.ReadAsync(envelope, source, component,
            _ => { RequireCompany(source.CompanyId); return Task.CompletedTask; }, token);
        RequireCompany(source.CompanyId);
        if (lineage.CompanyId != source.CompanyId || lineage.SourceEventId != source.EventId || lineage.InvoiceId != source.InvoiceId
            || lineage.InvoiceVersion != source.InvoiceVersion || lineage.AdmissionId != source.AdmissionId
            || lineage.ComponentKey != component.ComponentKey || lineage.ProductId != component.ProductId
            || lineage.SourceProofSha256 != component.SourceProofSha256 || lineage.RecordedRounding != component.RecordedRounding)
            throw Refuse("purchase_cost_attribution_source_conflict");
        var candidates = PurchaseFirstCostCandidatesV1.Build(lineage);
        var json = Encode(lineage); var at = clock.GetUtcNow();
        db.PurchaseCostAttributionSources.Add(new() { CompanyId = source.CompanyId, SourceEventId = source.EventId,
            InvoiceLineId = lineage.InvoiceLineId, InvoiceId = source.InvoiceId, ProductId = component.ProductId,
            SourceHash = component.SourceProofSha256, LineageHash = AccountingInventoryCostSourceValidationV1.Sha256(json),
            LineageJson = json, RecordedAtUtc = at });
        foreach (var candidate in candidates)
            db.PurchaseCostAttributionCandidates.Add(new() { CompanyId = source.CompanyId, SourceEventId = source.EventId,
                InvoiceLineId = lineage.InvoiceLineId, AcceptanceId = candidate.AcceptanceId, ReceiptMovementId = candidate.ReceiptMovementId,
                RootLabelId = candidate.RootLabelId, MovementId = candidate.Shipment.MovementId,
                MovementLineNumber = candidate.Shipment.MovementLineNumber, ShipmentId = candidate.Shipment.ShipmentId,
                ProductId = candidate.ProductId, SourceHash = candidate.Fingerprint, ViewJson = Encode(candidate), RecordedAtUtc = at });
        return candidates.Count > 0;
    }

    private async Task RecordPurchaseApproval(AccountingInventoryCostProposalV1 view, Func<CancellationToken, Task> fence, CancellationToken token)
    {
        if (!AccountingInventoryCostTraceV1.IsPurchaseFirstLineComponent(view.Component.ComponentKey)) return;
        var rows = await db.PurchaseCostAttributionSources.AsNoTracking().Where(x => x.SourceEventId == view.EventId).ToListAsync(token);
        await fence(token); RequireCompany(view.CompanyId);
        var row = rows.SingleOrDefault(x => AccountingInventoryCostTraceV1.PurchaseFirstLineComponent(x.InvoiceLineId) == view.Component.ComponentKey)
            ?? throw Refuse("purchase_cost_attribution_source_missing");
        var lineage = Decode<PurchaseFirstCostLineageV1>(row.LineageJson);
        if (row.CompanyId != view.CompanyId || row.InvoiceId != view.InvoiceId || row.ProductId != view.Component.ProductId
            || row.SourceHash != view.Component.SourceProofSha256 || row.LineageHash != AccountingInventoryCostSourceValidationV1.Sha256(row.LineageJson)
            || lineage.CompanyId != row.CompanyId || lineage.SourceEventId != row.SourceEventId || lineage.InvoiceLineId != row.InvoiceLineId
            || lineage.SourceProofSha256 != row.SourceHash || lineage.RecordedRounding != view.Component.RecordedRounding
            || view.Preparation is not { Rounding: { } } prepared || view.ApprovedBy is not { } approvedBy
            || prepared.PreparedBy == approvedBy || view.ApprovedAtUtc is not { } approvedAt || view.Status is not ("Posted" or "RoundedNoCost")
            || view.Status == "Posted" && view.JournalId is null || view.Status == "RoundedNoCost" && (view.JournalId is not null || prepared.SignedAmount != "0"))
            throw Refuse("purchase_cost_attribution_approval_invalid");
        var candidates = PurchaseFirstCostCandidatesV1.Build(lineage);
        var saved = await db.PurchaseCostAttributionCandidates.AsNoTracking().Where(x => x.SourceEventId == view.EventId
            && x.InvoiceLineId == row.InvoiceLineId).ToListAsync(token);
        await fence(token); RequireCompany(view.CompanyId);
        if (saved.Count != candidates.Count || candidates.Any(x => !saved.Any(y => y.AcceptanceId == x.AcceptanceId
            && y.MovementId == x.Shipment.MovementId && y.MovementLineNumber == x.Shipment.MovementLineNumber
            && y.SourceHash == x.Fingerprint && y.ViewJson == Encode(x)))) throw Refuse("purchase_cost_attribution_source_conflict");
        var approval = new PurchaseFirstCostApprovalV1(view.CompanyId, view.EventId, row.InvoiceLineId, view.ProposalId,
            view.Version, view.JournalId, view.Status, prepared.SignedAmount, prepared.CurrencyId, prepared.MoneyProfileId,
            prepared.MoneyProfileVersion, prepared.MoneyScale, prepared.Classification.AccountId, prepared.CounterAccount.AccountId,
            prepared.AccountingDate, prepared.PreparedBy, approvedBy, approvedAt, lineage.RecordedRounding, row.SourceHash, "");
        approval = approval with { Fingerprint = AccountingInventoryCostSourceValidationV1.Sha256(Encode(approval)) };
        var groups = candidates.GroupBy(x => (x.Shipment.MovementId, x.Shipment.MovementLineNumber)).ToArray();
        var zero = AccountingInventoryCostAmountsV1.FromDecimal(0m);
        var weighted = new List<PostedCostAttributionExactSourceV1>();
        foreach (var group in groups)
        {
            var physical = group.First().Shipment;
            if (group.Any(x => x.Shipment.ShipmentId != physical.ShipmentId || x.Shipment.PhysicalQuantityKg != physical.PhysicalQuantityKg
                || x.Shipment.RecordedAtUtc != physical.RecordedAtUtc || x.Shipment.ProvenOriginalSequence != physical.ProvenOriginalSequence
                || x.Shipment.ProductId != physical.ProductId || x.Shipment.LabelId != physical.LabelId
                || x.Shipment.LoadingInstructionLineId != physical.LoadingInstructionLineId)) throw Refuse("purchase_cost_attribution_physical_conflict");
            var kg = group.Aggregate(zero, (total, x) => AccountingInventoryCostAmountsV1.Add(total, x.InvoicedNetQuantityKg));
            var exact = group.Aggregate(zero, (total, x) => AccountingInventoryCostAmountsV1.Add(total, x.ExactTlAmount));
            if (kg == zero) { if (exact != zero) throw Refuse("purchase_cost_attribution_quantity_invalid"); continue; }
            weighted.Add(new(new(physical.ShipmentId, physical.MovementId, physical.MovementLineNumber, kg,
                physical.RecordedAtUtc, physical.ProvenOriginalSequence), exact));
        }
        var totalKg = weighted.Aggregate(zero, (total, x) => AccountingInventoryCostAmountsV1.Add(total, x.Source.NetQuantityKg));
        if (!decimal.TryParse(prepared.SignedAmount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            throw Refuse("purchase_cost_attribution_amount_unavailable");
        PostedCostAttributionCalculationV1? allocation;
        if (weighted.Count == 0)
        {
            // A fully returned, actually recorded shipment retains its zero witness. No positive-weight
            // distribution or zero journal is invented, and an all-stock source without shipments is not a claim.
            if (groups.Length == 0 || amount != 0 || view.Status != "RoundedNoCost" || view.JournalId is not null
                || totalKg != zero || candidates.Any(x => x.InvoicedNetQuantityKg != zero || x.ExactTlAmount != zero))
                throw Refuse("purchase_cost_attribution_amount_unavailable");
            allocation = new(0m, prepared.CurrencyId, prepared.MoneyScale, zero, []);
        }
        else if (!PostedManufacturingCostAttributionCalculatorV1.TryCalculatePurchase(amount, prepared.CurrencyId, prepared.MoneyScale,
                     totalKg, weighted, out allocation, out var reason)) throw Refuse(reason ?? "purchase_cost_attribution_amount_unavailable");
        var shares = new List<PostedPurchaseCostShareV1>();
        foreach (var group in groups)
        {
            var physical = group.First().Shipment;
            var calculated = allocation!.Shares.SingleOrDefault(x => x.Source.MovementId == physical.MovementId
                && x.Source.MovementLineNumber == physical.MovementLineNumber);
            var exact = calculated?.ExactSignedAmount ?? zero; var rounded = calculated?.RoundedSignedAmount ?? 0m;
            var residue = AccountingInventoryCostAmountsV1.Add(exact, AccountingInventoryCostAmountsV1.FromDecimal(-rounded));
            var share = new PostedPurchaseCostShareV1(approval, physical.ShipmentId, physical.MovementId, physical.MovementLineNumber,
                physical.ProductId, physical.PhysicalQuantityKg, rounded.ToString("0.############################", CultureInfo.InvariantCulture),
                exact, residue, group.ToArray(), "");
            shares.Add(share with { Fingerprint = AccountingInventoryCostSourceValidationV1.Sha256(Encode(share)) });
        }
        var proof = new PurchaseFirstCostPostedAttributionV1(approval, shares.OrderBy(x => x.ShipmentId).ThenBy(x => x.MovementLineNumber).ToArray());
        db.PurchaseCostAttributionApprovals.Add(new() { CompanyId = view.CompanyId, SourceEventId = view.EventId,
            InvoiceLineId = row.InvoiceLineId, ProposalId = view.ProposalId, ProposalVersion = view.Version, JournalId = view.JournalId,
            Status = view.Status == "Posted" ? 3 : 6, SourceHash = approval.Fingerprint, ViewJson = Encode(proof), RecordedAtUtc = clock.GetUtcNow() });
        await fence(token); RequireCompany(view.CompanyId);
    }
}
