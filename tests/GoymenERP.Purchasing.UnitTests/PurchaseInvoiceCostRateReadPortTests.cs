using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// BS-1 (F-011), the Purchasing side without a database: the invoice cost rate read port refuses an unresolved company context and a
/// company other than the context's BEFORE any connection is opened (the connection string points nowhere, so reaching the database
/// would be a provider exception, not these answers), answers an empty receipt identity NotFound without a connection, and its answer
/// can only be built in the three shapes the contract allows — Rate only with a positive rate from the recorded order currency,
/// Unavailable only with a known reason and no rate. What it answers on real rows (every recorded receipt Unavailable until the
/// supplier invoice exists, the order currency echoed, another company NotFound, nothing written) is proven on SQL Server
/// (<c>PurchasingSqlServerRelationalAcceptanceTests.InvoiceCostRate</c>). SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseInvoiceCostRateReadPortTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Receipt = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");
    private static readonly Guid OrderA = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    private static readonly Guid LineA = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
    private static readonly Guid Usd = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Try = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");

    [Fact]
    public async Task An_unresolved_company_context_is_refused_before_any_connection()
    {
        var (context, port) = Port(resolved: false);
        await using (context)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(CompanyA, Receipt));
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(Guid.Empty, Receipt));
        }
    }

    [Fact]
    public async Task A_company_other_than_the_resolved_context_is_refused_before_any_connection()
    {
        var (context, port) = Port(resolved: true);
        await using (context)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(CompanyB, Receipt));
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(Guid.Empty, Receipt));
        }
    }

    [Fact]
    public async Task An_empty_goods_receipt_line_is_not_found_without_any_connection()
    {
        var (context, port) = Port(resolved: true);
        await using (context)
        {
            var answer = await port.GetAsync(CompanyA, Guid.Empty);
            Assert.Equal(PurchaseInvoiceCostRateOutcomeV1.NotFound, answer.Outcome);
            Assert.Equal((CompanyA, Guid.Empty), (answer.CompanyId, answer.GoodsReceiptLineId));
            Assert.Null(answer.PurchaseOrderId);
            Assert.Null(answer.PurchaseOrderLineId);
            Assert.Null(answer.OrderCurrencyId);
            Assert.Null(answer.OrderCurrencyCode);
            Assert.Null(answer.Rate);
            Assert.Null(answer.UnavailableReason);
        }
    }

    [Fact]
    public async Task A_cancelled_read_is_cancelled_before_any_check()
    {
        var (context, port) = Port(resolved: false);
        await using (context)
        {
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => port.GetAsync(CompanyA, Receipt, cancelled.Token));
        }
    }

    [Fact]
    public void Unavailable_carries_the_reason_and_the_recorded_currency_but_never_a_rate()
    {
        var withCurrency = PurchaseInvoiceCostRateAnswerV1.Unavailable(CompanyA, Receipt, OrderA, LineA, Usd, "USD",
            PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded);
        Assert.Equal(PurchaseInvoiceCostRateOutcomeV1.Unavailable, withCurrency.Outcome);
        Assert.Equal((CompanyA, Receipt, (Guid?)OrderA, (Guid?)LineA, (Guid?)Usd, "USD"),
            (withCurrency.CompanyId, withCurrency.GoodsReceiptLineId, withCurrency.PurchaseOrderId, withCurrency.PurchaseOrderLineId,
                withCurrency.OrderCurrencyId, withCurrency.OrderCurrencyCode));
        Assert.Null(withCurrency.Rate);
        Assert.Equal("purchase_invoice_not_recorded", withCurrency.UnavailableReason);

        // An order without a money snapshot names no currency; the receipt still reads Unavailable (no provisional rate, BS-1).
        var withoutCurrency = PurchaseInvoiceCostRateAnswerV1.Unavailable(CompanyA, Receipt, OrderA, LineA, null, null,
            PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded);
        Assert.Null(withoutCurrency.OrderCurrencyId);
        Assert.Null(withoutCurrency.OrderCurrencyCode);
        Assert.Null(withoutCurrency.Rate);
    }

    [Fact]
    public void Unavailable_refuses_an_unknown_reason_a_half_currency_and_an_empty_identity()
    {
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoiceCostRateAnswerV1.Unavailable(CompanyA, Receipt, OrderA, LineA, Usd, "USD", "order_day_rate"));
        Assert.Throws<ArgumentException>(() => PurchaseInvoiceCostRateAnswerV1.Unavailable(CompanyA, Receipt, OrderA, LineA, Usd, "USD", ""));
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoiceCostRateAnswerV1.Unavailable(CompanyA, Receipt, OrderA, LineA, Usd, null, PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded));
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoiceCostRateAnswerV1.Unavailable(CompanyA, Receipt, OrderA, LineA, null, "USD", PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded));
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoiceCostRateAnswerV1.Unavailable(CompanyA, Receipt, OrderA, LineA, Guid.Empty, "USD", PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded));
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoiceCostRateAnswerV1.Unavailable(Guid.Empty, Receipt, OrderA, LineA, null, null, PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded));
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoiceCostRateAnswerV1.Unavailable(CompanyA, Guid.Empty, OrderA, LineA, null, null, PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded));
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoiceCostRateAnswerV1.Unavailable(CompanyA, Receipt, Guid.Empty, LineA, null, null, PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded));
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoiceCostRateAnswerV1.Unavailable(CompanyA, Receipt, OrderA, Guid.Empty, null, null, PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded));
        Assert.Throws<ArgumentException>(() => PurchaseInvoiceCostRateAnswerV1.NotFound(Guid.Empty, Receipt));
    }

    [Fact]
    public void Rated_carries_the_invoice_rate_from_the_order_currency_and_no_reason()
    {
        var rate = ExchangeRate.Of(Usd, Try, 34.5678m);
        var answer = PurchaseInvoiceCostRateAnswerV1.Rated(CompanyA, Receipt, OrderA, LineA, Usd, "USD", rate);

        Assert.Equal(PurchaseInvoiceCostRateOutcomeV1.Rate, answer.Outcome);
        Assert.Equal((ExchangeRate?)rate, answer.Rate);
        Assert.Equal((Guid?)Usd, answer.OrderCurrencyId);
        Assert.Null(answer.UnavailableReason);
    }

    [Fact]
    public void Rated_refuses_a_rate_whose_source_is_not_the_order_currency_or_that_is_not_a_real_rate()
    {
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoiceCostRateAnswerV1.Rated(CompanyA, Receipt, OrderA, LineA, Usd, "USD", ExchangeRate.Of(Try, Usd, 0.03m)));
        Assert.Throws<ArgumentException>(() => PurchaseInvoiceCostRateAnswerV1.Rated(CompanyA, Receipt, OrderA, LineA, Usd, "USD", default));
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoiceCostRateAnswerV1.Rated(CompanyA, Receipt, OrderA, LineA, Guid.Empty, "USD", ExchangeRate.Of(Usd, Try, 34m)));
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoiceCostRateAnswerV1.Rated(CompanyA, Receipt, OrderA, LineA, Usd, " ", ExchangeRate.Of(Usd, Try, 34m)));
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoiceCostRateAnswerV1.Rated(CompanyA, Guid.Empty, OrderA, LineA, Usd, "USD", ExchangeRate.Of(Usd, Try, 34m)));
    }

    [Fact]
    public void The_answer_carries_no_price_amount_or_quantity_member_and_keeps_its_member_order()
    {
        string[] names = [.. typeof(PurchaseInvoiceCostRateAnswerV1).GetProperties().Select(property => property.Name)];
        Assert.Equal(
            ["Outcome", "CompanyId", "GoodsReceiptLineId", "PurchaseOrderId", "PurchaseOrderLineId", "OrderCurrencyId", "OrderCurrencyCode",
                "Rate", "UnavailableReason"],
            names);
        string[] barred = ["price", "amount", "quantity", "discount", "payment", "money", "tax", "cost", "provisional"];
        Assert.DoesNotContain(names, name => barred.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));

        // No public constructor: only the three factories build an answer.
        Assert.Empty(typeof(PurchaseInvoiceCostRateAnswerV1).GetConstructors());
    }

    [Fact]
    public void The_contract_values_are_pinned()
    {
        Assert.Equal(1, (int)PurchaseInvoiceCostRateOutcomeV1.Rate);
        Assert.Equal(2, (int)PurchaseInvoiceCostRateOutcomeV1.Unavailable);
        Assert.Equal(3, (int)PurchaseInvoiceCostRateOutcomeV1.NotFound);
        Assert.Equal(3, Enum.GetValues<PurchaseInvoiceCostRateOutcomeV1>().Length);
        Assert.Equal("purchase_invoice_not_recorded", PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded);
        Assert.Equal(["purchase_invoice_not_recorded", "purchase_invoice_not_finalized", "purchase_invoice_coverage_incomplete",
            "purchase_invoice_multiple_official_rates", "purchase_invoice_no_foreign_currency_rate"], PurchaseInvoiceCostRateReasonsV1.All);
    }

    [Theory]
    [InlineData("10", "0", "10", null)]
    [InlineData("10", "0", "8", PurchaseInvoiceCostRateReasonsV1.InvoiceCoverageIncomplete)]
    [InlineData("10", "2", "8", null)]
    [InlineData("10", "2", "7", PurchaseInvoiceCostRateReasonsV1.InvoiceCoverageIncomplete)]
    [InlineData("10", "2", "10", null)]
    [InlineData("10", "10", "0", PurchaseInvoiceCostRateReasonsV1.InvoiceCoverageIncomplete)]
    [InlineData("10", "10", "10", PurchaseInvoiceCostRateReasonsV1.InvoiceCoverageIncomplete)]
    public void ALS_D127_the_rate_is_final_once_the_remaining_quantity_after_undo_or_return_is_wholly_invoiced(
        string accepted, string undone, string covered, string? reason)
    {
        Assert.Equal(reason, PurchaseInvoiceCostRateReadPort.CoverageRefusal(D(accepted), D(undone), D(covered), hasPending: false));
    }

    [Fact]
    public void ALS_D127_a_pending_invoice_keeps_the_rate_unavailable_and_an_impossible_coverage_is_unreadable()
    {
        Assert.Equal(PurchaseInvoiceCostRateReasonsV1.InvoiceNotFinalized,
            PurchaseInvoiceCostRateReadPort.CoverageRefusal(10m, 2m, 8m, hasPending: true));
        Assert.Throws<InvalidOperationException>(() => PurchaseInvoiceCostRateReadPort.CoverageRefusal(10m, 11m, 0m, false));
        Assert.Throws<InvalidOperationException>(() => PurchaseInvoiceCostRateReadPort.CoverageRefusal(10m, 0m, 11m, false));
        Assert.Throws<InvalidOperationException>(() => PurchaseInvoiceCostRateReadPort.CoverageRefusal(10m, -1m, 5m, false));
    }

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static (PurchasingDbContext Context, PurchaseInvoiceCostRateReadPort Port) Port(bool resolved)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        return (context, new PurchaseInvoiceCostRateReadPort(context, company));
    }
}
