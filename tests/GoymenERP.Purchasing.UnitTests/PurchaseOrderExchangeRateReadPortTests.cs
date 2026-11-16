using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// FNS-D73, the Purchasing side without a database: the purchase order exchange rate read port refuses an unresolved company context
/// and a company other than the context's BEFORE any connection is opened (the connection string points nowhere, so reaching the
/// database would be a provider exception, not these answers), answers an empty order identity NotFound without a connection, and
/// its answer can only be built in the shapes the contract allows — Rated only with a version, the order's recorded currency as the
/// rate's source and a positive rate to another currency; Unavailable only with a known reason (a version for the read reasons,
/// nothing read for a stored inconsistency); NotFound with neither. What it answers on real rows (a Draft OrderNotApproved, an
/// approved order RateNotSpecified with its currency echoed, another company NotFound, nothing written) is proven on SQL Server
/// (<c>PurchasingSqlServerRelationalAcceptanceTests.OrderExchangeRate</c>). SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseOrderExchangeRateReadPortTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid OrderA = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    private static readonly Guid Usd = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Try = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");

    [Fact]
    public async Task An_unresolved_company_context_is_refused_before_any_connection()
    {
        var (context, port) = Port(resolved: false);
        await using (context)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(CompanyA, OrderA));
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(Guid.Empty, OrderA));
        }
    }

    [Fact]
    public async Task A_company_other_than_the_resolved_context_is_refused_before_any_connection()
    {
        var (context, port) = Port(resolved: true);
        await using (context)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(CompanyB, OrderA));
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(Guid.Empty, OrderA));
        }
    }

    [Fact]
    public async Task An_empty_order_identity_is_not_found_without_any_connection()
    {
        var (context, port) = Port(resolved: true);
        await using (context)
        {
            var answer = await port.GetAsync(CompanyA, Guid.Empty);
            Assert.Equal(PurchaseOrderExchangeRateOutcomeV1.NotFound, answer.Outcome);
            Assert.Equal((CompanyA, Guid.Empty), (answer.CompanyId, answer.PurchaseOrderId));
            Assert.Null(answer.OrderVersion);
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
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => port.GetAsync(CompanyA, OrderA, cancelled.Token));
        }
    }

    [Fact]
    public void Rate_not_specified_echoes_the_read_order_version_and_its_recorded_currency_or_none()
    {
        var foreign = PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, 3, Usd, "USD",
            PurchaseOrderExchangeRateReasonsV1.RateNotSpecified);
        Assert.Equal(PurchaseOrderExchangeRateOutcomeV1.Unavailable, foreign.Outcome);
        Assert.Equal(((long?)3, (Guid?)Usd, "USD"), (foreign.OrderVersion, foreign.OrderCurrencyId, foreign.OrderCurrencyCode));
        Assert.Null(foreign.Rate);
        Assert.Equal("purchase_order_exchange_rate_not_specified", foreign.UnavailableReason);

        var noMoney = PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, 1, null, null,
            PurchaseOrderExchangeRateReasonsV1.RateNotSpecified);
        Assert.Null(noMoney.OrderCurrencyId);
        Assert.Null(noMoney.OrderCurrencyCode);

        var draft = PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, 1, Usd, "USD",
            PurchaseOrderExchangeRateReasonsV1.OrderNotApproved);
        Assert.Equal("purchase_order_not_approved", draft.UnavailableReason);

        var stored = PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, null, null, null,
            PurchaseOrderExchangeRateReasonsV1.StoredOrderUnreadable);
        Assert.Null(stored.OrderVersion);
        Assert.Null(stored.OrderCurrencyId);
        Assert.Equal("purchase_order_unreadable", stored.UnavailableReason);
    }

    [Fact]
    public void Unavailable_refuses_an_unknown_reason_a_wrong_version_a_half_currency_and_an_empty_identity()
    {
        var notSpecified = PurchaseOrderExchangeRateReasonsV1.RateNotSpecified;
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, 1, Usd, "USD", "tcmb_rate"));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, 1, Usd, "USD", ""));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, null, Usd, "USD", notSpecified));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, 0, Usd, "USD", notSpecified));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, 1, Usd, null, notSpecified));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, 1, null, "USD", notSpecified));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, 1, Guid.Empty, "USD", notSpecified));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, 1, Usd, " ", notSpecified));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, 1, null, null,
            PurchaseOrderExchangeRateReasonsV1.StoredOrderUnreadable));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, OrderA, null, Usd, "USD",
            PurchaseOrderExchangeRateReasonsV1.StoredOrderUnreadable));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Unavailable(Guid.Empty, OrderA, 1, Usd, "USD", notSpecified));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Unavailable(CompanyA, Guid.Empty, 1, Usd, "USD", notSpecified));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.NotFound(Guid.Empty, OrderA));
    }

    [Fact]
    public void Rated_carries_a_rate_from_the_order_currency_and_refuses_any_other()
    {
        var rate = ExchangeRate.Of(Usd, Try, 34.5678m);
        var rated = PurchaseOrderExchangeRateAnswerV1.Rated(CompanyA, OrderA, 2, Usd, "USD", rate);
        Assert.Equal(PurchaseOrderExchangeRateOutcomeV1.Rate, rated.Outcome);
        Assert.Equal(((long?)2, (Guid?)Usd, "USD"), (rated.OrderVersion, rated.OrderCurrencyId, rated.OrderCurrencyCode));
        Assert.Equal((ExchangeRate?)rate, rated.Rate);
        Assert.Null(rated.UnavailableReason);

        // The rate's source must be the order's recorded currency; a default (empty) rate or a version below one is refused.
        Assert.Throws<ArgumentException>(() =>
            PurchaseOrderExchangeRateAnswerV1.Rated(CompanyA, OrderA, 2, Usd, "USD", ExchangeRate.Of(Try, Usd, 0.03m)));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Rated(CompanyA, OrderA, 2, Usd, "USD", default));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseOrderExchangeRateAnswerV1.Rated(CompanyA, OrderA, 0, Usd, "USD", rate));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Rated(CompanyA, OrderA, 2, Guid.Empty, "USD", rate));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Rated(CompanyA, OrderA, 2, Usd, "", rate));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Rated(Guid.Empty, OrderA, 2, Usd, "USD", rate));
        Assert.Throws<ArgumentException>(() => PurchaseOrderExchangeRateAnswerV1.Rated(CompanyA, Guid.Empty, 2, Usd, "USD", rate));
    }

    [Fact]
    public void The_answer_keeps_its_member_order_and_carries_no_base_currency_or_converted_member()
    {
        string[] names = [.. typeof(PurchaseOrderExchangeRateAnswerV1).GetProperties().Select(property => property.Name)];
        Assert.Equal(
            ["Outcome", "CompanyId", "PurchaseOrderId", "OrderVersion", "OrderCurrencyId", "OrderCurrencyCode", "Rate",
                "UnavailableReason"],
            names);

        // FNS-D73: Purchasing answers the order's rate only; it never picks a rate kind or a functional currency for the consumer.
        string[] barred = ["base", "functional", "local", "converted", "tcmb", "bulletin", "kind", "needed"];
        Assert.DoesNotContain(names, name => barred.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));

        Assert.Empty(typeof(PurchaseOrderExchangeRateAnswerV1).GetConstructors());
    }

    [Fact]
    public void The_contract_values_are_pinned()
    {
        Assert.Equal(1, (int)PurchaseOrderExchangeRateOutcomeV1.Rate);
        Assert.Equal(2, (int)PurchaseOrderExchangeRateOutcomeV1.Unavailable);
        Assert.Equal(3, (int)PurchaseOrderExchangeRateOutcomeV1.NotFound);
        Assert.Equal(3, Enum.GetValues<PurchaseOrderExchangeRateOutcomeV1>().Length);
        string[] reasons = ["purchase_order_not_approved", "purchase_order_exchange_rate_not_specified", "purchase_order_unreadable"];
        Assert.Equal(reasons, PurchaseOrderExchangeRateReasonsV1.All.ToArray());
    }

    private static (PurchasingDbContext Context, PurchaseOrderExchangeRateReadPort Port) Port(bool resolved)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        return (context, new PurchaseOrderExchangeRateReadPort(context, company));
    }
}
