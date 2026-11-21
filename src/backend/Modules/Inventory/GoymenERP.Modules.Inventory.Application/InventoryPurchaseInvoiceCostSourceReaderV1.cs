using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Lossless projection of frozen Purchasing amounts. A quantity variance approved for invoice acceptance
/// does not decide a cost allocation policy. Such a source remains explicitly unreadable here.</summary>
public static class InventoryPurchaseInvoiceCostSourceReaderV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static InventoryPurchaseInvoiceCostProjectionV1 Read(WorkInboxEventV1 envelope, ICompanyContext company)
    {
        if (envelope.SourceName != InventoryPurchaseInvoiceCostInboxV1.SourceName
            || envelope.EventType != InventoryPurchaseInvoiceCostInboxV1.EventType || envelope.Version != 1)
            throw Invalid("purchase_invoice_cost_event_unsupported");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || company.CompanyId != envelope.CompanyId)
            throw Invalid("purchase_invoice_cost_company_mismatch");
        if (envelope.EventId == Guid.Empty || envelope.OccurredAtUtc == default || envelope.OccurredAtUtc.Offset != TimeSpan.Zero
            || !Text(envelope.CorrelationId, 128)) throw Invalid("purchase_invoice_cost_envelope_invalid");

        InventoryPurchaseInvoiceCostSourceV1? source;
        try { source = JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostSourceV1>(envelope.PayloadJson, Json); }
        catch (JsonException) { throw Invalid("purchase_invoice_cost_source_invalid"); }
        if (source is null || source.CompanyId != envelope.CompanyId || source.InvoiceId == Guid.Empty
            || source.Version < 1 || source.AdmissionId == Guid.Empty || source.CurrencyId == Guid.Empty
            || source.InvoiceDate == default || !Text(source.FinalizedBy, 160)
            || source.Lines is not { Count: > 0 and <= 10000 }) throw Invalid("purchase_invoice_cost_source_invalid");
        var doc = source.OfficialDocument;
        if (doc is null || doc.DocumentId == Guid.Empty || doc.DocumentVersion < 1 || doc.Ettn == Guid.Empty
            || doc.Sha256 is not { Length: 64 } || !doc.Sha256.All(char.IsAsciiHexDigit))
            throw Invalid("purchase_invoice_cost_document_unreadable");
        var isTl = source.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira;
        if (source.Net is null or < 0m || (isTl ? source.FunctionalNet is not null : source.FunctionalNet is null or < 0m))
            throw Invalid("purchase_invoice_cost_amount_unreadable");

        var lineIds = new HashSet<Guid>(); var lineNumbers = new HashSet<int>(); var acceptanceIds = new HashSet<Guid>();
        var net = ExactCostRatio.Of(0m); var tlNet = ExactCostRatio.Of(0m);
        var results = new List<InventoryPurchaseInvoiceCostReceiptV1>();
        foreach (var line in source.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || !lineIds.Add(line.LineId) || line.LineNo < 1
                || !lineNumbers.Add(line.LineNo) || !Text(line.ItemType, 128) || line.Net is null or < 0m
                || (isTl ? line.FunctionalNet is not null : line.FunctionalNet is null or < 0m)
                || line.LinePurpose is not ("production-input" or "resale" or "service" or "expense"))
                throw Invalid("purchase_invoice_cost_line_invalid");
            net = net.Add(ExactCostRatio.Of(line.Net.Value));
            var lineTl = ExactCostRatio.Of(isTl ? line.Net.Value : line.FunctionalNet!.Value);
            tlNet = tlNet.Add(lineTl);
            if (line.LinePurpose is "service" or "expense")
            {
                if (line.GoodsCostSource is not null) throw Invalid("purchase_invoice_cost_non_goods_source");
                continue;
            }
            var goods = line.GoodsCostSource;
            if (goods is null || goods.ProductId == Guid.Empty || goods.UnitId == Guid.Empty || goods.InvoiceQuantity <= 0m
                || goods.Receipts is not { Count: > 0 and <= 10000 }) throw Invalid("purchase_invoice_cost_goods_source_unreadable");
            var quantity = ExactCostRatio.Of(0m);
            foreach (var receipt in goods.Receipts)
            {
                if (receipt is null || receipt.AcceptanceId == Guid.Empty || !acceptanceIds.Add(receipt.AcceptanceId)
                    || receipt.OrderId == Guid.Empty || receipt.OrderLineId == Guid.Empty || receipt.OrderVersion < 1
                    || receipt.MatchedQuantity <= 0m || receipt.AcceptedQuantity <= 0m
                    || receipt.MatchedQuantity > receipt.AcceptedQuantity)
                    throw Invalid("purchase_invoice_cost_receipt_source_invalid");
                if (receipt.ReceiptAdjustments is { } adjustments && (adjustments.Count > 10000
                    || adjustments.Any(x => x is null || x.AdjustmentId == Guid.Empty || x.UndoQuantity <= 0m
                        || x.UndoQuantity > receipt.AcceptedQuantity)
                    || adjustments.Select(x => x.AdjustmentId).Distinct().Count() != adjustments.Count
                    || adjustments.Aggregate(ExactCostRatio.Of(receipt.MatchedQuantity),
                        (sum, x) => sum.Add(ExactCostRatio.Of(x.UndoQuantity))).CompareTo(ExactCostRatio.Of(receipt.AcceptedQuantity)) > 0))
                    throw Invalid("purchase_invoice_cost_receipt_adjustment_invalid");
                quantity = quantity.Add(ExactCostRatio.Of(receipt.MatchedQuantity));
                results.Add(new(line.LineId, line.LineNo, goods.ProductId, goods.UnitId, receipt.AcceptanceId,
                    receipt.OrderId, receipt.OrderLineId, receipt.OrderVersion, receipt.MatchedQuantity, receipt.AcceptedQuantity,
                    InventoryExactCostRatioV1.From(lineTl.Multiply(receipt.MatchedQuantity).Divide(goods.InvoiceQuantity)),
                    receipt.ReceiptAdjustments));
            }
            if (quantity.CompareTo(ExactCostRatio.Of(goods.InvoiceQuantity)) != 0)
                throw Invalid("purchase_invoice_cost_source_quantity_mismatch");
        }
        if (net.CompareTo(ExactCostRatio.Of(source.Net.Value)) != 0
            || tlNet.CompareTo(ExactCostRatio.Of(isTl ? source.Net.Value : source.FunctionalNet!.Value)) != 0)
            throw Invalid("purchase_invoice_cost_line_total_mismatch");
        return new(source.CompanyId, source.InvoiceId, source.Version, source.AdmissionId, source.InvoiceDate,
            source.FinalizedBy!, doc, results.OrderBy(x => x.InvoiceLineNo).ThenBy(x => x.AcceptanceId).ToArray());
    }

    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= max && value == value.Trim();
    private static OutboxPermanentFailureException Invalid(string code) => new(code, "Alış faturası maliyet kaynağı doğrulanamadı.");
}
