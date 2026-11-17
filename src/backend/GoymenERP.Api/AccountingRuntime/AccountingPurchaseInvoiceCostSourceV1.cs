using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.AccountingRuntime;

/// <summary>Adapts Inventory's committed valuation result only; does not re-run valuation or choose an account/rounding.</summary>
public static partial class AccountingPurchaseInvoiceCostSourceV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static AccountingInventoryCostSourceV1 Read(WorkInboxEventV1 envelope, Guid companyId)
    {
        AccountingInventoryCostSourceValidationV1.RequireEnvelope(envelope, companyId);
        if (envelope.SourceName != AccountingInventoryCostInboxV1.InventorySourceName) throw Invalid();
        InventoryPurchaseInvoiceCostApplicationResultV1? source;
        try { source = JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostApplicationResultV1>(envelope.PayloadJson, Json); }
        catch (JsonException) { throw Invalid(); }
        if (source is null || source.CompanyId != companyId || source.CostDifferenceEventId != envelope.EventId
            || source.InvoiceId == Guid.Empty || source.InvoiceVersion < 1 || source.SourceEventId == Guid.Empty
            || source.AdmissionId == Guid.Empty || source.InvoiceDate == default || !Hash(source.SourceSha256)
            || source.OfficialDocument is not { DocumentVersion: > 0 } official || official.DocumentId == Guid.Empty
            || official.Ettn == Guid.Empty || !Hash(official.Sha256)
            || source.Receipts is not { Count: <= 10000 } || source.Positions is not { Count: <= 10000 }
            || source.InitialRecognitions is { Count: > 10000 }
            || source.Receipts.Count == 0 && source.InitialRecognitions is not { Count: > 0 }) throw Invalid();
        var receiptIds = new HashSet<(Guid, Guid)>();
        foreach (var receipt in source.Receipts)
        {
            if (receipt is null || receipt.InvoiceLineId == Guid.Empty || receipt.InvoiceLineNo < 1 || receipt.AcceptanceId == Guid.Empty
                || receipt.MovementId == Guid.Empty || receipt.MovementLineNumber < 1 || receipt.LabelId == Guid.Empty
                || receipt.ProductId == Guid.Empty || receipt.LocationId == Guid.Empty || receipt.MatchedQuantity <= 0
                || receipt.AcceptedQuantity < receipt.MatchedQuantity || receipt.ReceiptCostStampVersion < 1
                || !receiptIds.Add((receipt.InvoiceLineId, receipt.AcceptanceId)) || Sign(receipt.InvoicedKg) <= 0
                || Sign(receipt.InvoiceTlValue) < 0 || Sign(receipt.PreviouslyRecognizedTlValue) < 0
                || Add(receipt.PreviouslyRecognizedTlValue, receipt.SourceDifference) != Ratio(receipt.InvoiceTlValue)) throw Invalid();
        }
        var positionIds = new HashSet<(Guid, Guid)>();
        var components = new List<AccountingInventoryCostComponentV1>();
        var sum = new AccountingInventoryCostRatioV1("0", "1");
        foreach (var position in source.Positions)
        {
            if (position is null || position.ProductId == Guid.Empty || position.LocationId == Guid.Empty || position.PoolVersion < 0
                || !positionIds.Add((position.ProductId, position.LocationId))) throw Invalid();
            var receipts = source.Receipts.Where(x => x.ProductId == position.ProductId && x.LocationId == position.LocationId).ToArray();
            if (receipts.Length == 0) throw Invalid();
            var receiptDifference = receipts.Aggregate(new AccountingInventoryCostRatioV1("0", "1"),
                (total, receipt) => AccountingInventoryCostAmountsV1.Add(total, Ratio(receipt.SourceDifference)));
            if (receiptDifference != Ratio(position.SourceDifference)
                || Add(position.AbsorbedDifference, position.UnabsorbedDifference) != Ratio(position.SourceDifference)
                || Add(position.AbsorbedDifference, position.AverageRoundingDifference) != Ratio(position.RecordedPoolValueEffect)) throw Invalid();
            var residual = Ratio(position.UnabsorbedDifference);
            sum = AccountingInventoryCostAmountsV1.Add(sum, residual);
            if (Sign(position.UnabsorbedDifference) == 0) continue;
            var proof = JsonSerializer.Serialize(new { source.CompanyId, source.InvoiceId, source.InvoiceVersion, source.SourceEventId,
                source.AdmissionId, source.InvoiceDate, source.OfficialDocument, source.SourceSha256, source.CostDifferenceEventId,
                position, receipts }, Json);
            components.Add(new(AccountingInventoryCostTraceV1.PurchaseComponent(position.ProductId, position.LocationId), position.ProductId,
                null, null, position.LocationId, residual, proof, AccountingInventoryCostSourceValidationV1.Sha256(proof)));
        }
        if (source.Receipts.Any(x => !positionIds.Contains((x.ProductId, x.LocationId)))) throw Invalid();
        sum = ReadFirstRecognitions(source, receiptIds, components, sum);
        if (sum != Ratio(source.UnabsorbedTlDifference)) throw Invalid();
        var result = new AccountingInventoryCostSourceV1(companyId, envelope.EventId, AccountingInventoryCostKindV1.PurchaseInvoiceDifference,
            source.InvoiceId, source.InvoiceVersion, source.AdmissionId, source.InvoiceDate, null, null,
            AccountingInventoryCostSourceValidationV1.Sha256(envelope.PayloadJson), components);
        AccountingInventoryCostSourceValidationV1.Require(envelope, result, companyId);
        return result;
    }
    private static AccountingInventoryCostRatioV1 Ratio(InventoryExactCostRatioV1? source)
    {
        var result = source is null ? null : new AccountingInventoryCostRatioV1(source.Numerator, source.Denominator);
        return AccountingInventoryCostAmountsV1.TryRead(result, out _, out _) ? result! : throw Invalid();
    }
    private static int Sign(InventoryExactCostRatioV1 value)
    { AccountingInventoryCostAmountsV1.TryRead(Ratio(value), out var numerator, out _); return numerator.Sign; }
    private static AccountingInventoryCostRatioV1 Add(InventoryExactCostRatioV1 left, InventoryExactCostRatioV1 right) =>
        AccountingInventoryCostAmountsV1.Add(Ratio(left), Ratio(right));
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static OutboxPermanentFailureException Invalid() => AccountingInventoryCostSourceValidationV1.Invalid("inventory_purchase_cost_proof_invalid");
}
