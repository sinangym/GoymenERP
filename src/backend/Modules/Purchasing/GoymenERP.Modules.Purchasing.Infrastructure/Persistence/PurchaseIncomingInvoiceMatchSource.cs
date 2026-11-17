using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>Reads only real Purchasing receipts/prices under the invoice store's order locks.
/// ALS-D14's previous-business-day algorithm remains exclusively in the Settings resolver.</summary>
public sealed class PurchaseIncomingInvoiceMatchSource(PurchasingDbContext context, ICompanyContext company,
    IPurchaseAcceptanceSourceV1 acceptances, IPurchaseOrderLineCostReadPortV1 prices,
    ISettingsPurchaseInvoiceVariancePolicyProviderV1 policies, ISettingsExchangeRateResolverV1 rates)
    : IPurchaseIncomingInvoiceMatchSourceV1
{
    public async Task<PurchaseIncomingInvoiceMatchSourceV1> ReadAsync(PurchaseIncomingInvoice invoice,
        IReadOnlyList<PurchaseInvoiceReceiptSelectionV1> selected, CancellationToken cancellationToken = default)
    {
        RequireScope(invoice.CompanyId);
        // No lock-free preview is allowed to become an authoritative match.
        if (context.Database.CurrentTransaction is null) throw Unavailable();
        SettingsPurchaseInvoiceVariancePolicyV1 policy;
        try { policy = await policies.GetAsync(invoice.CompanyId, cancellationToken); }
        catch (SettingsPrerequisiteException exception)
        { RequireScope(invoice.CompanyId); throw SettingsFailure(exception); }
        RequireScope(invoice.CompanyId);
        if (policy is null || policy.CompanyId != invoice.CompanyId || policy.PolicyId == Guid.Empty || policy.Version < 1
            || policy.AmountTolerance < 0 || policy.RateTolerance < 0) throw Unavailable();

        var matches = new List<PurchaseInvoiceReceiptMatch>();
        foreach (var item in selected)
        {
            cancellationToken.ThrowIfCancellationRequested(); RequireScope(invoice.CompanyId);
            var line = invoice.Lines.SingleOrDefault(line => line.Id == item.InvoiceLineId) ?? throw Mismatch();
            var quantity = Quantity(item.Quantity);
            PurchaseAcceptanceSourceV1? accepted;
            try { accepted = await acceptances.ReadAsync(item.PurchaseOrderId, item.AcceptanceId, cancellationToken); }
            catch (PurchaseAcceptanceParticipantException)
            { RequireScope(invoice.CompanyId); throw Unavailable(); }
            RequireScope(invoice.CompanyId);
            if (accepted is null || accepted.CompanyId != invoice.CompanyId || accepted.PurchaseOrderId != item.PurchaseOrderId
                || accepted.AcceptanceId != item.AcceptanceId || accepted.PurchaseOrderLineId == Guid.Empty) throw Mismatch();
            var order = await context.Orders.AsNoTracking().Where(row => row.Id == item.PurchaseOrderId)
                .Select(row => new { row.CompanyId, row.Id, row.Version, row.SupplierPartyId, row.CompanyAccountId, row.CurrencyId })
                .SingleOrDefaultAsync(cancellationToken);
            RequireScope(invoice.CompanyId);
            var price = await prices.GetAsync(invoice.CompanyId, item.PurchaseOrderId, accepted.PurchaseOrderLineId, cancellationToken);
            RequireScope(invoice.CompanyId);
            if (order is null || order.CompanyId != invoice.CompanyId || order.SupplierPartyId != invoice.SupplierPartyId
                || order.CompanyAccountId != invoice.CompanyAccountId || order.CurrencyId != invoice.CurrencyId
                || price.CompanyId != invoice.CompanyId || price.PurchaseOrderId != item.PurchaseOrderId
                || price.PurchaseOrderLineId != accepted.PurchaseOrderLineId) throw Mismatch();
            if (price.Outcome != PurchaseOrderLineCostOutcomeV1.Known || price.Price is not { } value
                || price.OrderVersion != order.Version) throw Unavailable();
            if (value.ProductId != line.ProductId || value.LineUnitId != line.UnitId || value.PriceUnitId != line.UnitId
                || value.UnitPrice.CurrencyId != invoice.CurrencyId) throw Mismatch();
            var day = BusinessDay(accepted.OccurredAtUtc);
            decimal? control = null;
            if (invoice.OfficialRate is not null)
            {
                ExchangeRateResolutionV1 answer;
                try { answer = await rates.ResolveAsync(day, invoice.CurrencyId, invoice.FunctionalCurrencyId, cancellationToken); }
                catch (SettingsPrerequisiteException exception)
                { RequireScope(invoice.CompanyId); throw SettingsFailure(exception); }
                RequireScope(invoice.CompanyId);
                if (answer is not { State: ExchangeRateResolutionStatesV1.Resolved, BulletinDate: { } bulletin, SellingRate: { } text }
                    || answer.RateDay != day || answer.SourceCurrencyId != invoice.CurrencyId || answer.TargetCurrencyId != invoice.FunctionalCurrencyId
                    || bulletin > day || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) || rate <= 0)
                    throw Unavailable();
                control = rate;
            }
            decimal expected;
            try { expected = CommercialLinePriceCalculator.ExactLineAmount(quantity, value.UnitPrice.Amount,
                value.DiscountPercent, value.DiscountAmount, value.DeferralPercent, value.DeferralAmount); }
            catch (Exception exception) when (exception is ArgumentException or OverflowException) { throw Unavailable(); }
            matches.Add(new(invoice.CompanyId, line.Id, accepted.AcceptanceId, order.Id, accepted.PurchaseOrderLineId,
                order.Version, order.SupplierPartyId, order.CompanyAccountId, invoice.CurrencyId, line.UnitId,
                quantity, expected, control, day));
        }
        RequireScope(invoice.CompanyId);
        return new(matches, new(policy.PolicyId, policy.Version, policy.AmountTolerance, policy.RateTolerance));
    }

    private void RequireScope(Guid companyId)
    {
        if (!company.IsResolved || company.CompanyId == Guid.Empty || company.CompanyId != companyId)
            throw new PurchasingServiceException(403, "forbidden");
    }
    private static decimal Quantity(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0
            || value > PurchaseIncomingInvoice.MaximumStoredQuantity || decimal.Round(value, PurchaseIncomingInvoice.MaximumStoredScale) != value
            || PurchaseAcceptanceParticipantContractV1.FormatQuantity(value) != text)
            throw new PurchasingServiceException(400, "validation_failed");
        return value;
    }
    private static DateOnly BusinessDay(DateTimeOffset instant)
    {
        try { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException) { throw Unavailable(); }
    }
    private static PurchasingServiceException SettingsFailure(SettingsPrerequisiteException exception) =>
        exception.Code == PurchaseInvoiceVariancePolicyCodesV1.AccessDenied || exception.Code == ExchangeRateProblemCodesV1.AccessDenied
            ? new(403, "forbidden") : Unavailable();
    private static PurchasingServiceException Unavailable() => new(503, "service_unavailable");
    private static PurchasingRuleException Mismatch() => new(PurchaseIncomingInvoice.SourceMismatch, "Fatura ve gerçek kabul kaynağı uyuşmuyor.");
}
