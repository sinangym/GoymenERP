using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>Reads a proved subset of the physical line. The caller combines actual roots and claims the whole line once.</summary>
public sealed class PurchasePostedCostAttributionStoreV1(AccountingDbContext db, ICompanyContext company)
    : IPostedPurchaseShipmentCostReaderV1
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<PurchaseShipmentCostAttributionReadV1> ReadProvenSharesAsync(Guid companyId, Guid shipmentId,
        Guid movementId, int movementLineNumber, Guid productId, decimal quantityKg,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw Refuse("purchase_cost_attribution_transaction_required");
        if (companyId != Company() || shipmentId == Guid.Empty || movementId == Guid.Empty || movementLineNumber < 1
            || productId == Guid.Empty || quantityKg <= 0) throw Refuse("purchase_cost_attribution_query_invalid");
        await Fence(companyId, freshConsumerFence, cancellationToken);
        var candidates = await db.PurchaseCostAttributionCandidates.AsNoTracking().Where(x => x.MovementId == movementId
            && x.MovementLineNumber == movementLineNumber).Take(10001).ToListAsync(cancellationToken);
        await Fence(companyId, freshConsumerFence, cancellationToken);
        if (candidates.Count > 10000) throw Refuse("purchase_cost_attribution_source_unavailable");
        var shares = new List<PostedPurchaseCostShareV1>();
        var pending = false;
        foreach (var group in candidates.GroupBy(x => (x.SourceEventId, x.InvoiceLineId)))
        {
            var sourceRow = await db.PurchaseCostAttributionSources.AsNoTracking().SingleOrDefaultAsync(x =>
                x.SourceEventId == group.Key.SourceEventId && x.InvoiceLineId == group.Key.InvoiceLineId, cancellationToken);
            await Fence(companyId, freshConsumerFence, cancellationToken);
            if (sourceRow is null || sourceRow.CompanyId != companyId || sourceRow.ProductId != productId
                || Hash(sourceRow.LineageJson) != sourceRow.LineageHash) throw Refuse("purchase_cost_attribution_source_unavailable");
            var lineage = Decode<PurchaseFirstCostLineageV1>(sourceRow.LineageJson);
            var actualSource = await db.InventoryCostSources.AsNoTracking().SingleOrDefaultAsync(x =>
                x.EventId == sourceRow.SourceEventId, cancellationToken);
            await Fence(companyId, freshConsumerFence, cancellationToken);
            if (actualSource is null) throw Refuse("purchase_cost_attribution_source_unavailable");
            var source = Decode<AccountingInventoryCostSourceV1>(actualSource.SourceJson);
            var component = source.Components.SingleOrDefault(x => x.ComponentKey == lineage.ComponentKey);
            if (source.CompanyId != companyId || source.EventId != sourceRow.SourceEventId
                || source.Kind != AccountingInventoryCostKindV1.PurchaseInvoiceDifference || source.InvoiceId != sourceRow.InvoiceId
                || source.InvoiceId != lineage.InvoiceId || source.InvoiceVersion != lineage.InvoiceVersion || source.AdmissionId != lineage.AdmissionId
                || source.SourcePayloadSha256 != actualSource.PayloadHash || component is null || component.ProductId != productId
                || lineage.CompanyId != companyId || lineage.SourceEventId != sourceRow.SourceEventId || lineage.InvoiceLineId != sourceRow.InvoiceLineId
                || lineage.SourceProofSha256 != sourceRow.SourceHash || component.SourceProofSha256 != sourceRow.SourceHash
                || Hash(component.SourceProofJson) != sourceRow.SourceHash || component.RecordedRounding != lineage.RecordedRounding)
                throw Refuse("purchase_cost_attribution_source_unavailable");
            var expected = PurchaseFirstCostCandidatesV1.Build(lineage).Where(x => x.Shipment.MovementId == movementId
                && x.Shipment.MovementLineNumber == movementLineNumber).OrderBy(x => x.AcceptanceId).ToArray();
            if (expected.Length != group.Count() || expected.Any(x => x.Shipment.ShipmentId != shipmentId || x.ProductId != productId
                    || x.Shipment.PhysicalQuantityKg != quantityKg || !group.Any(row => row.CompanyId == companyId
                        && row.AcceptanceId == x.AcceptanceId && row.ShipmentId == shipmentId && row.ProductId == productId
                        && row.ReceiptMovementId == x.ReceiptMovementId && row.RootLabelId == x.RootLabelId
                        && row.SourceHash == x.Fingerprint && row.ViewJson == Encode(x))))
                throw Refuse("purchase_cost_attribution_candidate_unavailable");
            var approvalRow = await db.PurchaseCostAttributionApprovals.AsNoTracking().SingleOrDefaultAsync(x =>
                x.SourceEventId == sourceRow.SourceEventId && x.InvoiceLineId == sourceRow.InvoiceLineId, cancellationToken);
            await Fence(companyId, freshConsumerFence, cancellationToken);
            if (approvalRow is null) { pending = true; continue; }
            var proof = Decode<PurchaseFirstCostPostedAttributionV1>(approvalRow.ViewJson);
            var approval = proof.Approval;
            if (approval.CompanyId != companyId || approval.SourceEventId != sourceRow.SourceEventId || approval.InvoiceLineId != sourceRow.InvoiceLineId
                || approval.Fingerprint != approvalRow.SourceHash || Hash(Encode(approval with { Fingerprint = "" })) != approval.Fingerprint
                || approval.ProposalId != approvalRow.ProposalId || approval.ProposalVersion != approvalRow.ProposalVersion
                || approval.JournalId != approvalRow.JournalId || approval.SourceProofSha256 != sourceRow.SourceHash
                || approval.RecordedRounding != lineage.RecordedRounding || approval.PreparedBy == Guid.Empty
                || approval.ApprovedBy == Guid.Empty || approval.PreparedBy == approval.ApprovedBy
                || approvalRow.Status != (approval.Status == "Posted" ? 3 : approval.Status == "RoundedNoCost" ? 6 : -1))
                throw Refuse("purchase_cost_attribution_approval_unavailable");
            var proposalRow = await db.InventoryCostProposals.AsNoTracking().Where(x => x.Id == approval.ProposalId)
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
            await Fence(companyId, freshConsumerFence, cancellationToken);
            if (proposalRow is null || proposalRow.Version != approval.ProposalVersion || proposalRow.Status != approvalRow.Status)
                throw Refuse("purchase_cost_attribution_approval_changed");
            var proposal = Decode<AccountingInventoryCostProposalV1>(proposalRow.ViewJson);
            if (proposal.CompanyId != companyId || proposal.EventId != approval.SourceEventId || proposal.ProposalId != approval.ProposalId
                || proposal.Version != approval.ProposalVersion || proposal.Status != approval.Status || proposal.JournalId != approval.JournalId
                || Encode(proposal.Component) != Encode(component) || proposal.ApprovedBy != approval.ApprovedBy || proposal.ApprovedAtUtc != approval.ApprovedAtUtc
                || proposal.Preparation is not { Rounding: { } } preparation || preparation.PreparedBy != approval.PreparedBy
                || preparation.SignedAmount != approval.SignedAmount || preparation.CurrencyId != approval.CurrencyId
                || preparation.MoneyProfileId != approval.MoneyProfileId || preparation.MoneyProfileVersion != approval.MoneyProfileVersion
                || preparation.MoneyScale != approval.MoneyScale || preparation.AccountingDate != approval.AccountingDate
                || preparation.Classification.AccountId != approval.CogsAccountId || preparation.CounterAccount.AccountId != approval.CounterAccountId)
                throw Refuse("purchase_cost_attribution_approval_unavailable");
            await RequireJournal(approval, companyId, freshConsumerFence, cancellationToken);
            var share = proof.Shares.SingleOrDefault(x => x.MovementId == movementId && x.MovementLineNumber == movementLineNumber);
            if (share is null || share.ShipmentId != shipmentId || share.ProductId != productId || share.PhysicalQuantityKg != quantityKg
                || share.Approval != approval || Hash(Encode(share with { Fingerprint = "" })) != share.Fingerprint
                || Encode(share.Candidates.OrderBy(x => x.AcceptanceId).ToArray()) != Encode(expected))
                throw Refuse("purchase_cost_attribution_share_unavailable");
            var sum = AccountingInventoryCostAmountsV1.FromDecimal(0m);
            foreach (var item in proof.Shares)
            {
                if (item.Approval != approval || Hash(Encode(item with { Fingerprint = "" })) != item.Fingerprint
                    || !Money(item.SignedAmount, out var amount)
                    || AccountingInventoryCostAmountsV1.Add(AccountingInventoryCostAmountsV1.FromDecimal(amount), item.ExactSignedResidue) != item.ExactSignedAmount)
                    throw Refuse("purchase_cost_attribution_share_unavailable");
                sum = AccountingInventoryCostAmountsV1.Add(sum, AccountingInventoryCostAmountsV1.FromDecimal(amount));
            }
            if (!Money(approval.SignedAmount, out var total) || sum != AccountingInventoryCostAmountsV1.FromDecimal(total)
                || proof.Shares.Select(x => (x.MovementId, x.MovementLineNumber)).Distinct().Count() != proof.Shares.Count)
                throw Refuse("purchase_cost_attribution_total_unavailable");
            shares.Add(share);
        }
        await Fence(companyId, freshConsumerFence, cancellationToken);
        return new PurchaseShipmentCostAttributionReadV1(companyId, shipmentId, movementId, movementLineNumber, productId, quantityKg, pending,
            shares.OrderBy(x => x.Approval.SourceEventId).ThenBy(x => x.Approval.InvoiceLineId).ToArray());
    }

    private async Task RequireJournal(PurchaseFirstCostApprovalV1 approval, Guid captured,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        if (!Money(approval.SignedAmount, out var amount)) throw Refuse("purchase_cost_attribution_amount_unavailable");
        if (approval.Status == "RoundedNoCost")
        {
            if (approval.JournalId is not null || amount != 0) throw Refuse("purchase_cost_attribution_posted_journal_unavailable");
            return;
        }
        if (approval.JournalId is not { } id || amount <= 0) throw Refuse("purchase_cost_attribution_posted_journal_unavailable");
        var header = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        await Fence(captured, fence, token);
        var lines = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == id).OrderBy(x => x.LineNumber).ToListAsync(token);
        await Fence(captured, fence, token);
        var reversed = await db.Journals.AnyAsync(x => x.LinkedJournalId == id && x.Kind == (int)JournalKind.Reversal, token);
        await Fence(captured, fence, token);
        var reversing = await db.JournalReversalProposals.AnyAsync(x => x.SourceJournalId == id && x.Status == 1, token);
        await Fence(captured, fence, token);
        if (header is null || header.CompanyId != captured || header.SourceId != approval.ProposalId
            || header.Kind != (int)JournalKind.Original || header.PostingPurpose != AccountingInventoryCostTraceV1.PurchaseDifferencePurpose
            || header.CurrencyId != approval.CurrencyId || header.AccountingDate != approval.AccountingDate
            || lines.Count != 2 || lines[0].LineNumber != 1 || lines[1].LineNumber != 2
            || lines[0].Debit != amount || lines[0].Credit != 0 || lines[1].Debit != 0 || lines[1].Credit != amount
            || lines[0].AccountId != approval.CogsAccountId || lines[1].AccountId != approval.CounterAccountId
            || approval.CogsAccountId == approval.CounterAccountId || reversed || reversing)
            throw Refuse("purchase_cost_attribution_posted_journal_unavailable");
    }

    private Guid Company() => company.IsResolved && company.CompanyId != Guid.Empty ? company.CompanyId : throw Refuse("company_context_mismatch");
    private async Task Fence(Guid captured, Func<CancellationToken, Task> fence, CancellationToken token)
    { ArgumentNullException.ThrowIfNull(fence); if (Company() != captured) throw Refuse("company_context_mismatch"); await fence(token); if (Company() != captured) throw Refuse("company_context_mismatch"); }
    private static bool Money(string text, out decimal value) => decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out value) && value >= 0 && value.ToString("0.############################", CultureInfo.InvariantCulture) == text;
    private static T Decode<T>(string text) => JsonSerializer.Deserialize<T>(text, Json) ?? throw Refuse("purchase_cost_attribution_proof_unreadable");
    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static string Hash(string text) => AccountingInventoryCostSourceValidationV1.Sha256(text);
    private static AccountingStoreException Refuse(string code) => new AccountingStoreException(code, "Kaydedilmiş alış maliyetinin gerçek sevk payı kanıtı bekleniyor.");
}
