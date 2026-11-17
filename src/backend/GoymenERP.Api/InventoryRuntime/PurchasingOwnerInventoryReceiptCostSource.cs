using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.InventoryRuntime;

/// <summary>Purchasing proof is already enlisted in the receipt host transaction. This adapter never creates a second
/// Purchasing connection, guesses a price coefficient, or substitutes order/day FX for the official invoice cost rate.</summary>
public sealed class PurchasingOwnerInventoryReceiptCostSource(IPurchaseReceiptCostProofV1 purchasing,
    IProductPurchaseUnitConversionV1 product, ISettingsExchangeRateResolverV1 exchangeRates,
    ICompanyContext company, ICurrentUser user) : IInventoryPurchaseReceiptCostSourceV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Source = "inventory.purchase-receipt-cost.v1";

    public async Task<InventoryPurchaseReceiptCostV1> ReadAsync(InventoryPurchaseReceiptCostSelectionV1 selection,
        CancellationToken cancellationToken = default)
    {
        var actor = user.UserId;
        Fence(selection.CompanyId, actor);
        PurchaseReceiptCostProofV1? proof = null;
        ProductPurchaseUnitConversionResultV1? conversion = null;
        ExchangeRateResolutionV1? usdRate = null;
        try
        {
            var receiptProof = await purchasing.ReadAsync(selection.PurchaseOrderId, selection.PurchaseOrderLineId, selection.AcceptanceId, cancellationToken);
            Fence(selection.CompanyId, actor);
            if (receiptProof is null || receiptProof.CompanyId != selection.CompanyId || receiptProof.AcceptanceId != selection.AcceptanceId
                || receiptProof.PurchaseOrderId != selection.PurchaseOrderId || receiptProof.PurchaseOrderLineId != selection.PurchaseOrderLineId
                || receiptProof.ProductId != selection.ProductId || receiptProof.ProductVersion != selection.ProductVersion
                || receiptProof.ReceivedAtUtc.Offset != TimeSpan.Zero || receiptProof.Price.CompanyId != selection.CompanyId
                || receiptProof.Price.PurchaseOrderId != selection.PurchaseOrderId || receiptProof.Price.PurchaseOrderLineId != selection.PurchaseOrderLineId
                || receiptProof.Price.Price is { } scopedPrice && scopedPrice.ProductId != selection.ProductId
                || receiptProof.InvoiceRate.CompanyId != selection.CompanyId || receiptProof.InvoiceRate.GoodsReceiptLineId != selection.AcceptanceId
                || receiptProof.InvoiceRate.PurchaseOrderId is { } invoiceOrder && invoiceOrder != selection.PurchaseOrderId
                || receiptProof.InvoiceRate.PurchaseOrderLineId is { } invoiceLine && invoiceLine != selection.PurchaseOrderLineId)
                return Unavailable("purchase_receipt_cost_proof_unreadable");
            // Rejected owner payloads never enter another company's immutable evidence.
            proof = receiptProof;
            if (proof.Price is not { Outcome: PurchaseOrderLineCostOutcomeV1.Known, Price: { } price } || price.ProductId != selection.ProductId)
                return Unavailable(proof.Price.UnreadableReason ?? "purchase_receipt_price_not_recorded");
            if (price.MovingAverageUnitCost <= 0m) return Unavailable("purchase_receipt_cost_zero_unreadable");
            var day = ProductPriceBusinessDayV2.DateOf(proof.ReceivedAtUtc);
            var unitConversion = await product.ConvertAsync(new(selection.ProductId, 1m, price.PriceUnitId, null, day), cancellationToken);
            Fence(selection.CompanyId, actor);
            if (unitConversion.Outcome != ProductPurchaseUnitConversionOutcomeV1.Available || unitConversion.Quantity is not > 0m
                || unitConversion.InputKg != unitConversion.Quantity || unitConversion.RemainderKg != 0m
                || unitConversion.Source is not { } unitSource || unitSource.CompanyId != selection.CompanyId
                || unitSource.ProductId != selection.ProductId || !unitSource.Units.Any(unit => unit.UnitId == price.PriceUnitId
                    && unit.KgPerUnit == unitConversion.Quantity))
                return Unavailable("purchase_receipt_unit_conversion_unreadable");
            conversion = unitConversion;
            var originalRatio = ExactCostRatio.Of(price.MovingAverageUnitCost).Divide(conversion.Quantity!.Value);
            decimal? original = originalRatio.TryToDecimal(out var originalScalar) ? originalScalar : null;
            var currency = price.UnitPrice.CurrencyId;
            var invoice = MapInvoice(proof.InvoiceRate);
            // Resolve the invoice's authority/currency with the actual price input. A non-terminating per-kg quotient
            // is kept as a rational value until the existing average's final 4dp rounding, never rounded here.
            var tl = InventoryAverageCostFxRateResolverV1.Resolve(selection.CompanyId, selection.AcceptanceId, currency, price.MovingAverageUnitCost, invoice);
            var hasTl = tl.Status is InventoryAverageCostFxRateStatusV1.BaseCurrency or InventoryAverageCostFxRateStatusV1.Converted
                || tl.UnavailableCode == InventoryAverageCostFxRateUnavailableCodesV1.NotRepresentable;
            var tlRatio = !hasTl ? null : currency == SettingsCurrencyIdentitiesV1.TurkishLira
                ? originalRatio : originalRatio.Multiply(invoice.Rate!.Value.Rate);
            decimal? tlScalar = tlRatio is not null && tlRatio.TryToDecimal(out var actualTl) ? actualTl : null;
            decimal? usd = null;
            ExactCostRatio? usdRatio = null;
            string? usdReason = null;
            if (currency == ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD")) { usd = original; usdRatio = originalRatio; }
            else if (currency == SettingsCurrencyIdentitiesV1.TurkishLira)
            {
                try
                {
                var resolvedUsdRate = await exchangeRates.ResolveAsync(day, ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"),
                    SettingsCurrencyIdentitiesV1.TurkishLira, cancellationToken);
                Fence(selection.CompanyId, actor);
                if (resolvedUsdRate.State == ExchangeRateResolutionStatesV1.Resolved && resolvedUsdRate.Source == ExchangeRateSourcesV1.Tcmb
                    && resolvedUsdRate.RateDay == day && resolvedUsdRate.SourceCurrencyId == ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD")
                    && resolvedUsdRate.TargetCurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira && resolvedUsdRate.RecordId is not null
                    && !string.IsNullOrWhiteSpace(resolvedUsdRate.RecordVersion) && resolvedUsdRate.BulletinDate is not null
                    && decimal.TryParse(resolvedUsdRate.SellingRate, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) && rate > 0m)
                {
                    usdRate = resolvedUsdRate;
                    usdRatio = originalRatio.Divide(rate);
                    if (usdRatio.TryToDecimal(out var exactUsd)) usd = exactUsd;
                }
                else usdReason = "purchase_receipt_usd_entry_rate_unreadable";
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (UnauthorizedAccessException) { throw; }
                catch { Fence(selection.CompanyId, actor); usdReason = "purchase_receipt_usd_entry_rate_unreadable"; }
            }
            else usdReason = "purchase_receipt_usd_invoice_cross_rate_not_recorded";
            var evidence = Evidence();
            var version = Version(evidence);
            var provenance = currency == SettingsCurrencyIdentitiesV1.TurkishLira
                ? new InventoryIssueCostCurrencyV1(InventoryIssueCostCurrencyStatusV1.FunctionalCurrency, null, null, null, version, null)
                : hasTl ? new(InventoryIssueCostCurrencyStatusV1.Converted, currency, original, invoice.Rate!.Value.Rate,
                    version, null, InventoryExactCostRatioV1.From(originalRatio))
                : new(InventoryIssueCostCurrencyStatusV1.RateMissing, currency, original, null,
                    version, tl.UnavailableCode ?? "purchase_receipt_invoice_rate_unreadable", InventoryExactCostRatioV1.From(originalRatio));
            Fence(selection.CompanyId, actor);
            return new(selection.CompanyId, selection.AcceptanceId, selection.PurchaseOrderId, selection.PurchaseOrderLineId,
                currency, original, tlScalar, usd, provenance, version, evidence, hasTl ? null : tl.UnavailableCode, usdReason,
                InventoryExactCostRatioV1.From(originalRatio), tlRatio is null ? null : InventoryExactCostRatioV1.From(tlRatio),
                usdRatio is null ? null : InventoryExactCostRatioV1.From(usdRatio));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch { Fence(selection.CompanyId, actor); return Unavailable("purchase_receipt_cost_source_unreadable"); }

        string Evidence() => JsonSerializer.Serialize(new { selection, proof, conversion, usdRate }, Json);
        InventoryPurchaseReceiptCostV1 Unavailable(string reason)
        {
            var evidence = Evidence(); var version = Version(evidence);
            return new(selection.CompanyId, selection.AcceptanceId, selection.PurchaseOrderId, selection.PurchaseOrderLineId,
                null, null, null, null, new(InventoryIssueCostCurrencyStatusV1.Unreadable, null, null, null, version, reason),
                version, evidence, reason, reason);
        }
    }

    private static InventoryPurchaseInvoiceCostRateV1 MapInvoice(PurchaseInvoiceCostRateAnswerV1 answer) => answer.Outcome switch
    {
        PurchaseInvoiceCostRateOutcomeV1.Rate when answer.Rate is { } rate && answer.OrderCurrencyId is { } currency
            && answer.PurchaseOrderId is { } order && answer.PurchaseOrderLineId is { } line =>
            InventoryPurchaseInvoiceCostRateV1.Rated(answer.CompanyId, answer.GoodsReceiptLineId, order, line, currency, rate),
        PurchaseInvoiceCostRateOutcomeV1.Unavailable when answer.UnavailableReason == PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded =>
            InventoryPurchaseInvoiceCostRateV1.Unavailable(answer.CompanyId, answer.GoodsReceiptLineId, answer.PurchaseOrderId,
                answer.PurchaseOrderLineId, answer.OrderCurrencyId, InventoryPurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded),
        _ => InventoryPurchaseInvoiceCostRateV1.Unavailable(answer.CompanyId, answer.GoodsReceiptLineId, answer.PurchaseOrderId,
            answer.PurchaseOrderLineId, answer.OrderCurrencyId, InventoryPurchaseInvoiceCostRateReasonsV1.Unreadable),
    };

    private void Fence(Guid companyId, string? actor)
    {
        if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId || string.IsNullOrWhiteSpace(actor)
            || !user.IsAuthenticated || user.UserId != actor || !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException();
    }
    private static string Version(string evidence) => Source + "/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence))).ToLowerInvariant();
}
