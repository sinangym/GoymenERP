using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Usd_shipments_on_different_business_days_wait_before_a_rate_or_invoice_is_written()
    {
        var (invoicing, request) = await ShipmentDayInvoiceAsync(SettingsCurrencyIdentitiesV1.UnitedStatesDollar,
            Shipped, Shipped.AddDays(1));
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(request, Metadata("SENTETIK-usd-different-days")));
        Assert.Equal((409, SalesInvoiceProblemCodesV1.InvoiceNotReady), Status(refused));
        Assert.Equal([new SalesDraftFieldError("shares[1]", SalesInvoiceReasonCodesV1.UsdShipmentDayMismatch)], refused.Errors);
        Assert.Empty(invoicing.Rates.Asked);
        Assert.Empty(invoicing.Invoices.Stored);
        Assert.Empty(invoicing.Invoices.Attempts);
    }

    [Fact]
    public async Task Usd_shipments_in_one_Istanbul_day_share_one_invoice_even_across_UTC_midnight()
    {
        var (invoicing, request) = await ShipmentDayInvoiceAsync(SettingsCurrencyIdentitiesV1.UnitedStatesDollar,
            new(2026, 10, 19, 21, 30, 0, TimeSpan.Zero), new(2026, 10, 20, 7, 30, 0, TimeSpan.Zero));
        var result = await invoicing.Service().CreateInvoiceAsync(request, Metadata("SENTETIK-usd-one-business-day"));
        Assert.Equal(2, result.Invoice.Lines.Count);
        Assert.Single(invoicing.Rates.Asked);
        Assert.Single(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task The_Usd_dispatch_day_rule_does_not_split_Try_shipments_by_an_unapproved_policy()
    {
        var (invoicing, request) = await ShipmentDayInvoiceAsync(SettingsCurrencyIdentitiesV1.TurkishLira,
            Shipped, Shipped.AddDays(1));
        var result = await invoicing.Service().CreateInvoiceAsync(request, Metadata("SENTETIK-try-different-days"));
        Assert.Equal(2, result.Invoice.Lines.Count);
        Assert.Null(result.Invoice.ExchangeRate);
        Assert.Empty(invoicing.Rates.Asked);
    }

    private static async Task<(InvoiceWorld Invoicing, CreateSalesInvoiceRequest Request)> ShipmentDayInvoiceAsync(
        Guid currency, DateTimeOffset firstDay, DateTimeOffset secondDay)
    {
        var seed = await InvoiceWorld.CreateAsync();
        var order = await ApprovedAcceptedAsync(seed.World, "SENTETIK-day-order",
            request => request with { CurrencyId = currency });
        var invoicing = InvoiceWorld.Around(seed.World, order);
        var secondShipment = Guid.NewGuid();
        var original = invoicing.Shipments.Answer;
        invoicing.Shipments.Answer = query =>
        {
            var answer = original(query);
            var first = answer.Shares[0] with { ShippedAtUtc = firstDay };
            return answer with { Shares = [first, first with { DispatchNoteId = Guid.NewGuid(),
                ShipmentId = secondShipment, ShippedAtUtc = secondDay }] };
        };
        return (invoicing, invoicing.Request() with { Shares = [new(invoicing.ShipmentId), new(secondShipment)] });
    }
}
