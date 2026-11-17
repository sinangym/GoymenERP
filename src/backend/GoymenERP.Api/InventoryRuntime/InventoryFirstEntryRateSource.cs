using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.InventoryRuntime;

/// <summary>FNS-D103 receipt-time proof. It does not admit a provisional TL stock cost or borrow the invoice's rate.</summary>
internal static class InventoryFirstEntryRateSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<InventoryFirstEntryRateV1> ReadAsync(PurchaseReceiptCostProofV1 proof,
        ISettingsExchangeRateResolverV1 settings, Action freshAuthority, CancellationToken token)
    {
        freshAuthority(); token.ThrowIfCancellationRequested();
        var day = ProductPriceBusinessDayV2.DateOf(proof.ReceivedAtUtc);
        var order = proof.ApprovedOrderRate;
        PurchaseOrderExchangeRateAnswerV1? admittedOrder = null;
        ExchangeRateResolutionV1? admittedDay = null;
        var currency = proof.Price.Price?.UnitPrice.CurrencyId;
        try
        {
            if (order is null) return Unavailable("first_entry_approved_order_rate_source_missing");
            if (order.CompanyId != proof.CompanyId || order.PurchaseOrderId != proof.PurchaseOrderId
                || order.OrderVersion is not > 0 || order.OrderVersion != proof.Price.OrderVersion
                || order.OrderCurrencyId == Guid.Empty || order.Outcome == PurchaseOrderExchangeRateOutcomeV1.NotFound
                || currency is { } priceCurrency && order.OrderCurrencyId != priceCurrency)
                return Unavailable("first_entry_approved_order_rate_source_unreadable");
            admittedOrder = order; currency = order.OrderCurrencyId ?? currency;
            if (currency == SettingsCurrencyIdentitiesV1.TurkishLira)
            {
                if (order.Outcome != PurchaseOrderExchangeRateOutcomeV1.Unavailable
                    || order.UnavailableReason != PurchaseOrderExchangeRateReasonsV1.RateNotSpecified)
                    return Unavailable("first_entry_approved_order_rate_source_unreadable");
                return Answer(InventoryFirstEntryRateStateV1.NotRequired, null,
                    InventoryFirstEntryRateSourcesV1.FunctionalCurrency, null);
            }
            if (currency is null) return Unavailable("first_entry_order_currency_not_recorded");
            if (order.Outcome == PurchaseOrderExchangeRateOutcomeV1.Rate)
            {
                if (order.Rate is not { } rate || rate.SourceCurrencyId != currency
                    || rate.TargetCurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira || rate.Rate <= 0m)
                    return Unavailable("first_entry_approved_order_rate_source_unreadable");
                return Answer(InventoryFirstEntryRateStateV1.Rated, rate.Rate,
                    InventoryFirstEntryRateSourcesV1.ApprovedPurchaseOrder, null);
            }
            if (order.Outcome != PurchaseOrderExchangeRateOutcomeV1.Unavailable
                || order.UnavailableReason != PurchaseOrderExchangeRateReasonsV1.RateNotSpecified)
                return Unavailable("first_entry_approved_order_rate_source_unreadable");
            var resolved = await settings.ResolveAsync(day, currency.Value, SettingsCurrencyIdentitiesV1.TurkishLira, token);
            freshAuthority(); token.ThrowIfCancellationRequested();
            if (resolved.State != ExchangeRateResolutionStatesV1.Resolved || resolved.Source != ExchangeRateSourcesV1.Tcmb
                || resolved.RateDay != day || resolved.SourceCurrencyId != currency
                || resolved.TargetCurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira || resolved.RecordId is null || resolved.RecordId == Guid.Empty
                || string.IsNullOrWhiteSpace(resolved.RecordVersion) || resolved.BulletinDate is null || resolved.BulletinDate > day
                || !decimal.TryParse(resolved.SellingRate, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0m)
                return Unavailable("first_entry_day_rate_source_unreadable");
            admittedDay = resolved;
            return Answer(InventoryFirstEntryRateStateV1.Rated, value, InventoryFirstEntryRateSourcesV1.EntryDayTcmb, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch { freshAuthority(); return Unavailable("first_entry_rate_source_unreadable"); }

        InventoryFirstEntryRateV1 Unavailable(string reason) => Answer(InventoryFirstEntryRateStateV1.Unavailable,
            null, InventoryFirstEntryRateSourcesV1.Unavailable, reason);
        InventoryFirstEntryRateV1 Answer(InventoryFirstEntryRateStateV1 state, decimal? value, string source, string? reason)
        {
            freshAuthority(); token.ThrowIfCancellationRequested();
            var evidence = JsonSerializer.Serialize(new { proof.CompanyId, proof.AcceptanceId, proof.PurchaseOrderId,
                entryDay = day, order = admittedOrder, dayRate = admittedDay, reason }, Json);
            var answer = new InventoryFirstEntryRateV1(proof.CompanyId, proof.AcceptanceId, proof.PurchaseOrderId, day, state, currency,
                SettingsCurrencyIdentitiesV1.TurkishLira, value, value is { } rate ? InventoryExactCostRatioV1.From(ExactCostRatio.Of(rate)) : null,
                source, string.Empty, evidence, reason, admittedOrder?.OrderVersion);
            return answer with { SourceVersion = InventoryFirstEntryRateAdmissionV1.SourceVersionOf(answer) };
        }
    }
}
