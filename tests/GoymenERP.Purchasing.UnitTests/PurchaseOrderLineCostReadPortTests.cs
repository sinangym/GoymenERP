using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// SAT-D295 with BS-1, the Purchasing side without a database: the order line cost read port refuses an unresolved company context
/// and a company other than the context's BEFORE any connection is opened (the connection string points nowhere, so reaching the
/// database would be a provider exception, not these answers), answers an empty order or line identity NotFound without a
/// connection, and its answer can only be built in the shapes the contract allows — Known only with a version and a consistent
/// price, Unreadable only with a known reason (a version for the read reasons, none for a stored inconsistency), NotFound with
/// neither. The price facts compute the exact discounted and net unit prices through the shared calculator and refuse an
/// inconsistent set. What it answers on real rows (a priced line Known, an unpriced line PricePending, a technical line priced on
/// its general line, a Draft OrderNotApproved, another company NotFound, nothing written) is proven on SQL Server
/// (<c>PurchasingSqlServerRelationalAcceptanceTests.OrderLineCost</c>). SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseOrderLineCostReadPortTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid OrderA = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    private static readonly Guid LineA = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
    private static readonly Guid TechnicalLineA = Guid.Parse("dddddddd-0000-4000-8000-000000000002");
    private static readonly Guid ProductA = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid UnitA = Guid.Parse("ffffffff-0000-4000-8000-000000000001");
    private static readonly Guid UnitB = Guid.Parse("ffffffff-0000-4000-8000-000000000002");
    private static readonly Guid Usd = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    [Fact]
    public async Task An_unresolved_company_context_is_refused_before_any_connection()
    {
        var (context, port) = Port(resolved: false);
        await using (context)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(CompanyA, OrderA, LineA));
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(Guid.Empty, OrderA, LineA));
        }
    }

    [Fact]
    public async Task A_company_other_than_the_resolved_context_is_refused_before_any_connection()
    {
        var (context, port) = Port(resolved: true);
        await using (context)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(CompanyB, OrderA, LineA));
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(Guid.Empty, OrderA, LineA));
        }
    }

    [Fact]
    public async Task An_empty_order_or_line_identity_is_not_found_without_any_connection()
    {
        var (context, port) = Port(resolved: true);
        await using (context)
        {
            var emptyLine = await port.GetAsync(CompanyA, OrderA, Guid.Empty);
            Assert.Equal(PurchaseOrderLineCostOutcomeV1.NotFound, emptyLine.Outcome);
            Assert.Equal((CompanyA, OrderA, Guid.Empty), (emptyLine.CompanyId, emptyLine.PurchaseOrderId, emptyLine.PurchaseOrderLineId));
            Assert.Null(emptyLine.OrderVersion);
            Assert.Null(emptyLine.Price);
            Assert.Null(emptyLine.UnreadableReason);

            var emptyOrder = await port.GetAsync(CompanyA, Guid.Empty, LineA);
            Assert.Equal(PurchaseOrderLineCostOutcomeV1.NotFound, emptyOrder.Outcome);
            Assert.Equal((CompanyA, Guid.Empty, LineA), (emptyOrder.CompanyId, emptyOrder.PurchaseOrderId, emptyOrder.PurchaseOrderLineId));
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
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => port.GetAsync(CompanyA, OrderA, LineA, cancelled.Token));
        }
    }

    [Fact]
    public void The_price_facts_compute_the_exact_discounted_and_net_unit_prices_and_never_round()
    {
        // 12.3456 − 10 % = 11.11104; + 2 % maturity surcharge on the discounted price = 11.3332608 (exact, more decimals than the price).
        var price = PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, Money.Of(12.3456m, Usd, 4), "USD", 10m, null,
            2m, null);

        Assert.Equal((LineA, ProductA, UnitA, UnitA, false), (price.PricedLineId, price.ProductId, price.LineUnitId, price.PriceUnitId,
            price.PriceUnitIsKilogram));
        Assert.Equal(12.3456m, price.UnitPrice.Amount);
        Assert.Equal(Usd, price.UnitPrice.CurrencyId);
        Assert.Equal("USD", price.CurrencyCode);
        Assert.Equal(((decimal?)10m, (decimal?)null, (decimal?)2m, (decimal?)null),
            (price.DiscountPercent, price.DiscountAmount, price.DeferralPercent, price.DeferralAmount));
        Assert.Equal(11.11104m, price.DiscountedUnitPrice);
        Assert.Equal(11.3332608m, price.NetUnitPrice);
        Assert.Equal(CommercialLinePriceCalculator.NetUnitPrice(12.3456m, 10m, null, 2m, null), price.NetUnitPrice);
    }

    [Fact]
    public void The_moving_average_unit_cost_is_the_discounted_unit_price_without_the_maturity_surcharge()
    {
        // ALS-D104 / SAT-D386: discounts applied, VAT excluded (the line price carries no tax), vade farkı excluded (SAT-D263 (3)).
        var percent = PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, Money.Of(12.3456m, Usd, 4), "USD", 10m,
            null, 2m, null);
        Assert.Equal(11.11104m, percent.MovingAverageUnitCost);
        Assert.Equal(percent.DiscountedUnitPrice, percent.MovingAverageUnitCost);
        Assert.NotEqual(percent.NetUnitPrice, percent.MovingAverageUnitCost);

        // 10 − 1.25 per unit = 8.75; the 0.5 per unit surcharge stays out of the cost.
        var amount = PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, Money.Of(10m, Usd, 2), "USD", null, 1.25m,
            null, 0.5m);
        Assert.Equal(8.75m, amount.MovingAverageUnitCost);
        Assert.Equal(9.25m, amount.NetUnitPrice);

        var plain = PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, true, Money.Of(7.5m, Usd, 2), "USD", null, null,
            null, null);
        Assert.Equal(7.5m, plain.MovingAverageUnitCost);
    }

    [Fact]
    public void A_price_without_discount_or_surcharge_answers_the_unit_price_as_both_derived_prices()
    {
        var price = PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, true, Money.Of(7.5m, Usd, 2), "USD", null, null,
            null, null);

        Assert.True(price.PriceUnitIsKilogram);
        Assert.Equal(7.5m, price.DiscountedUnitPrice);
        Assert.Equal(7.5m, price.NetUnitPrice);
    }

    [Fact]
    public void A_zero_unit_price_is_a_recorded_price()
    {
        // SAT-D297: how a zero-cost receipt counts is the consumer's rule; the port answers the recorded zero.
        var price = PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, Money.Zero(Usd, 2), "USD", null, null, null,
            null);

        Assert.True(price.UnitPrice.IsZero);
        Assert.Equal(0m, price.NetUnitPrice);
    }

    [Fact]
    public void The_price_facts_refuse_an_inconsistent_set()
    {
        var unitPrice = Money.Of(10m, Usd, 2);
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitB, false, unitPrice, "USD", null, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, Money.Of(-1m, Usd, 2), "USD", null, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, unitPrice, "USD", 5m, 1m, null, null));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, unitPrice, "USD", null, null, 2m, 1m));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, Money.Zero(Usd, 2), "USD", null, null, 2m, null));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, unitPrice, " USD", null, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, unitPrice, "", null, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(Guid.Empty, ProductA, UnitA, UnitA, false, unitPrice, "USD", null, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(LineA, Guid.Empty, UnitA, UnitA, false, unitPrice, "USD", null, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, Guid.Empty, Guid.Empty, false, unitPrice, "USD", null, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, default, "USD", null, null, null, null));

        // The calculator's own refusals: a percent outside 0..100 and a discount amount above the price.
        Assert.ThrowsAny<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, unitPrice, "USD", 101m, null, null, null));
        Assert.ThrowsAny<ArgumentException>(() =>
            PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, unitPrice, "USD", null, 11m, null, null));
    }

    [Fact]
    public void Known_carries_the_version_and_the_price_and_no_reason()
    {
        var price = PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, Money.Of(10m, Usd, 2), "USD", null, null,
            null, null);
        var answer = PurchaseOrderLineCostAnswerV1.Known(CompanyA, OrderA, TechnicalLineA, 3, price);

        Assert.Equal(PurchaseOrderLineCostOutcomeV1.Known, answer.Outcome);
        Assert.Equal((CompanyA, OrderA, TechnicalLineA, (long?)3), (answer.CompanyId, answer.PurchaseOrderId, answer.PurchaseOrderLineId,
            answer.OrderVersion));
        Assert.Same(price, answer.Price);
        Assert.Null(answer.UnreadableReason);
    }

    [Fact]
    public void Known_refuses_a_missing_price_a_version_below_one_and_an_empty_identity()
    {
        var price = PurchaseOrderLineCostPriceV1.Of(LineA, ProductA, UnitA, UnitA, false, Money.Of(10m, Usd, 2), "USD", null, null,
            null, null);
        Assert.Throws<ArgumentNullException>(() => PurchaseOrderLineCostAnswerV1.Known(CompanyA, OrderA, LineA, 1, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseOrderLineCostAnswerV1.Known(CompanyA, OrderA, LineA, 0, price));
        Assert.Throws<ArgumentException>(() => PurchaseOrderLineCostAnswerV1.Known(Guid.Empty, OrderA, LineA, 1, price));
        Assert.Throws<ArgumentException>(() => PurchaseOrderLineCostAnswerV1.Known(CompanyA, Guid.Empty, LineA, 1, price));
        Assert.Throws<ArgumentException>(() => PurchaseOrderLineCostAnswerV1.Known(CompanyA, OrderA, Guid.Empty, 1, price));
    }

    [Fact]
    public void Unreadable_carries_the_reason_a_version_for_the_read_reasons_and_never_a_price()
    {
        var notApproved = PurchaseOrderLineCostAnswerV1.Unreadable(CompanyA, OrderA, LineA, 1,
            PurchaseOrderLineCostReasonsV1.OrderNotApproved);
        Assert.Equal(PurchaseOrderLineCostOutcomeV1.Unreadable, notApproved.Outcome);
        Assert.Equal((long?)1, notApproved.OrderVersion);
        Assert.Null(notApproved.Price);
        Assert.Equal("purchase_order_not_approved", notApproved.UnreadableReason);

        var pending = PurchaseOrderLineCostAnswerV1.Unreadable(CompanyA, OrderA, LineA, 2, PurchaseOrderLineCostReasonsV1.PricePending);
        Assert.Equal((long?)2, pending.OrderVersion);
        Assert.Equal("purchase_order_line_price_pending", pending.UnreadableReason);

        var stored = PurchaseOrderLineCostAnswerV1.Unreadable(CompanyA, OrderA, LineA, null,
            PurchaseOrderLineCostReasonsV1.StoredLineUnreadable);
        Assert.Null(stored.OrderVersion);
        Assert.Null(stored.Price);
        Assert.Equal("purchase_order_line_unreadable", stored.UnreadableReason);
    }

    [Fact]
    public void Unreadable_refuses_an_unknown_reason_a_wrong_version_and_an_empty_identity()
    {
        Assert.Throws<ArgumentException>(() => PurchaseOrderLineCostAnswerV1.Unreadable(CompanyA, OrderA, LineA, 1, "order_day_price"));
        Assert.Throws<ArgumentException>(() => PurchaseOrderLineCostAnswerV1.Unreadable(CompanyA, OrderA, LineA, 1, ""));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostAnswerV1.Unreadable(CompanyA, OrderA, LineA, null, PurchaseOrderLineCostReasonsV1.PricePending));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostAnswerV1.Unreadable(CompanyA, OrderA, LineA, 0, PurchaseOrderLineCostReasonsV1.OrderNotApproved));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostAnswerV1.Unreadable(CompanyA, OrderA, LineA, 1, PurchaseOrderLineCostReasonsV1.StoredLineUnreadable));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostAnswerV1.Unreadable(Guid.Empty, OrderA, LineA, 1, PurchaseOrderLineCostReasonsV1.PricePending));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostAnswerV1.Unreadable(CompanyA, Guid.Empty, LineA, 1, PurchaseOrderLineCostReasonsV1.PricePending));
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderLineCostAnswerV1.Unreadable(CompanyA, OrderA, Guid.Empty, 1, PurchaseOrderLineCostReasonsV1.PricePending));
        Assert.Throws<ArgumentException>(() => PurchaseOrderLineCostAnswerV1.NotFound(Guid.Empty, OrderA, LineA));
    }

    [Fact]
    public void The_answer_and_the_price_keep_their_member_order_and_carry_no_rate_or_base_currency_member()
    {
        string[] answerNames = [.. typeof(PurchaseOrderLineCostAnswerV1).GetProperties().Select(property => property.Name)];
        Assert.Equal(
            ["Outcome", "CompanyId", "PurchaseOrderId", "PurchaseOrderLineId", "OrderVersion", "Price", "UnreadableReason"],
            answerNames);

        string[] priceNames = [.. typeof(PurchaseOrderLineCostPriceV1).GetProperties().Select(property => property.Name)];
        Assert.Equal(
            ["PricedLineId", "ProductId", "LineUnitId", "PriceUnitId", "PriceUnitIsKilogram", "UnitPrice", "CurrencyCode",
                "DiscountPercent", "DiscountAmount", "DeferralPercent", "DeferralAmount", "DiscountedUnitPrice", "NetUnitPrice",
                "MovingAverageUnitCost"],
            priceNames);

        // BS-1: the rate is the invoice's (IPurchaseInvoiceCostRateReadPortV1); nothing here converts or picks a functional currency.
        string[] barred = ["rate", "base", "functional", "local", "converted", "provisional", "quantity", "tax"];
        Assert.DoesNotContain(answerNames.Concat(priceNames), name => barred.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));

        // No public constructor: only the factories build an answer or a price.
        Assert.Empty(typeof(PurchaseOrderLineCostAnswerV1).GetConstructors());
        Assert.Empty(typeof(PurchaseOrderLineCostPriceV1).GetConstructors());
    }

    [Fact]
    public void The_contract_values_are_pinned()
    {
        Assert.Equal(1, (int)PurchaseOrderLineCostOutcomeV1.Known);
        Assert.Equal(2, (int)PurchaseOrderLineCostOutcomeV1.NotFound);
        Assert.Equal(3, (int)PurchaseOrderLineCostOutcomeV1.Unreadable);
        Assert.Equal(3, Enum.GetValues<PurchaseOrderLineCostOutcomeV1>().Length);
        string[] reasons = ["purchase_order_not_approved", "purchase_order_line_price_pending", "purchase_order_line_unreadable"];
        Assert.Equal(reasons, PurchaseOrderLineCostReasonsV1.All.ToArray());
    }

    private static (PurchasingDbContext Context, PurchaseOrderLineCostReadPort Port) Port(bool resolved)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        return (context, new PurchaseOrderLineCostReadPort(context, company));
    }
}
