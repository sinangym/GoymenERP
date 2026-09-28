using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// DEC-FIN-016 risk tracking (user decision 2026-09-27), pure: every received instrument lands in exactly one risk state
/// as of a date, the open exposure is the not-collected-not-returned part, sums are exact text per currency, and a
/// withheld amount grant hides every amount. SENTETIK.
/// </summary>
public sealed class CustomerInstrumentRiskPolicyTests
{
    private static readonly Guid PartyA = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid AccountA = Guid.Parse("a2000000-0000-4000-8000-000000000002");
    private static readonly Guid PartyB = Guid.Parse("b1000000-0000-4000-8000-000000000001");
    private static readonly Guid AccountB = Guid.Parse("b2000000-0000-4000-8000-000000000002");
    private static readonly Guid Lira = Guid.Parse("c1000000-0000-4000-8000-000000000001");
    private static readonly Guid Dollar = Guid.Parse("c2000000-0000-4000-8000-000000000002");
    private static readonly DateOnly AsOf = new(2026, 10, 1);
    private static readonly IReadOnlyDictionary<Guid, int> Scales = new Dictionary<Guid, int> { [Lira] = 2, [Dollar] = 3 };

    [Theory]
    [InlineData(CustomerInstrumentStatus.InPortfolio, "2026-10-01", CustomerInstrumentRiskState.InPortfolioNotDue)]
    [InlineData(CustomerInstrumentStatus.InPortfolio, "2026-09-30", CustomerInstrumentRiskState.InPortfolioOverdue)]
    [InlineData(CustomerInstrumentStatus.AtBankForCollection, "2026-09-01", CustomerInstrumentRiskState.AtBankForCollection)]
    [InlineData(CustomerInstrumentStatus.Bounced, "2026-10-15", CustomerInstrumentRiskState.Dishonoured)]
    [InlineData(CustomerInstrumentStatus.Protested, "2026-10-15", CustomerInstrumentRiskState.Dishonoured)]
    [InlineData(CustomerInstrumentStatus.ReturnedToCustomer, "2026-09-01", CustomerInstrumentRiskState.ReturnedToCustomer)]
    [InlineData(CustomerInstrumentStatus.Collected, "2026-09-01", CustomerInstrumentRiskState.Collected)]
    public void Each_status_has_one_risk_state_and_the_portfolio_splits_by_due_date(CustomerInstrumentStatus status,
        string dueOn, CustomerInstrumentRiskState expected)
    {
        Assert.Equal(expected, CustomerInstrumentRisk.StateOf(status, DateOnly.Parse(dueOn), AsOf));
        Assert.Equal(expected is not (CustomerInstrumentRiskState.ReturnedToCustomer or CustomerInstrumentRiskState.Collected),
            CustomerInstrumentRisk.IsOpenExposure(expected));
    }

    [Fact]
    public void Buckets_per_customer_and_currency_with_exact_sums_and_a_total_per_currency()
    {
        var facts = new[]
        {
            Fact(PartyA, AccountA, Lira, CustomerInstrumentStatus.InPortfolio, "2026-10-15", "1000.00"),
            Fact(PartyA, AccountA, Lira, CustomerInstrumentStatus.InPortfolio, "2026-09-15", "250.5"),
            Fact(PartyA, AccountA, Lira, CustomerInstrumentStatus.AtBankForCollection, "2026-10-15", "300"),
            Fact(PartyA, AccountA, Lira, CustomerInstrumentStatus.Bounced, "2026-10-15", "0.01"),
            Fact(PartyA, AccountA, Lira, CustomerInstrumentStatus.Collected, "2026-09-10", "700.00"),
            Fact(PartyA, AccountA, Lira, CustomerInstrumentStatus.ReturnedToCustomer, "2026-09-10", "90"),
            Fact(PartyB, AccountB, Lira, CustomerInstrumentStatus.InPortfolio, "2026-12-01", "5000"),
            Fact(PartyB, AccountB, Dollar, CustomerInstrumentStatus.Protested, "2026-12-01", "12.345"),
        };

        var (customers, totals) = CustomerInstrumentRiskPolicy.Build(facts, AsOf, AmountDisclosure.Visible, Scales);

        Assert.Equal(3, customers.Count);
        var a = Assert.Single(customers, line => line.PartyId == PartyA).Figures;
        Assert.Equal((1, "1000.00"), Read(a.InPortfolioNotDue));
        // P3-1: every sum is written at the currency scale (2 for the lira here), never with fewer digits.
        Assert.Equal((1, "250.50"), Read(a.InPortfolioOverdue));
        Assert.Equal((1, "300.00"), Read(a.AtBankForCollection));
        Assert.Equal((1, "0.01"), Read(a.Dishonoured));
        Assert.Equal((1, "90.00"), Read(a.ReturnedToCustomer));
        Assert.Equal((1, "700.00"), Read(a.Collected));
        // Open exposure: everything received, not collected and not returned — 1000.00 + 250.5 + 300 + 0.01.
        Assert.Equal((4, "1550.51"), Read(a.OpenExposure));

        // Currencies are never added: one total per currency, over every customer line.
        Assert.Equal(new[] { Lira, Dollar }.Order(), totals.Select(total => total.CurrencyId).Order());
        var lira = Assert.Single(totals, total => total.CurrencyId == Lira).Figures;
        Assert.Equal((2, "6000.00"), Read(lira.InPortfolioNotDue));
        Assert.Equal((5, "6550.51"), Read(lira.OpenExposure));
        var dollar = Assert.Single(totals, total => total.CurrencyId == Dollar).Figures;
        Assert.Equal((1, "12.345"), Read(dollar.Dishonoured));
        // An empty bucket is a zero at the currency scale ("0.000" for a scale-3 currency), not a bare "0".
        Assert.Equal((0, "0.000"), Read(dollar.Collected));
        Assert.Equal((0, "0.00"),
            Read(Assert.Single(customers, line => line.PartyId == PartyB && line.CurrencyId == Lira).Figures.Collected));
        Assert.All(totals.SelectMany(Buckets).Concat(customers.SelectMany(line => Buckets(line.Figures))),
            bucket => Assert.Equal(AmountDisclosure.Visible, bucket.Amount.Disclosure));
    }

    [Theory]
    [InlineData(AmountDisclosure.Redacted)]
    [InlineData(AmountDisclosure.Unavailable)]
    public void Without_the_amount_grant_every_amount_is_withheld_and_only_counts_remain(AmountDisclosure disclosure)
    {
        var (customers, totals) = CustomerInstrumentRiskPolicy.Build(
            [Fact(PartyA, AccountA, Lira, CustomerInstrumentStatus.InPortfolio, "2026-10-15", "1000.00")], AsOf, disclosure,
            Scales);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new CustomerInstrumentRiskView(Guid.NewGuid(), AsOf, null, null, customers, totals), FinanceJson.CreateOptions()));
        var views = AmountViews(document.RootElement).ToList();
        Assert.Equal(2 * 7, views.Count);
        Assert.All(views, view =>
        {
            Assert.Equal(disclosure.ToString(), view.GetProperty("disclosure").GetString());
            Assert.Equal(JsonValueKind.Null, view.GetProperty("money").ValueKind);
        });
        Assert.Equal(1, customers.Single().Figures.OpenExposure.Count);
    }

    [Fact]
    public void An_unreadable_stored_amount_is_a_broken_invariant_not_a_zero() =>
        Assert.Throws<InvalidOperationException>(() => CustomerInstrumentRiskPolicy.Build(
            [Fact(PartyA, AccountA, Lira, CustomerInstrumentStatus.InPortfolio, "2026-10-15", "1,000")], AsOf,
            AmountDisclosure.Visible, Scales));

    [Fact]
    public void A_sum_wider_than_the_display_scale_keeps_every_digit()
    {
        var (customers, _) = CustomerInstrumentRiskPolicy.Build(
            [Fact(PartyA, AccountA, Dollar, CustomerInstrumentStatus.InPortfolio, "2026-10-15", "12.345")], AsOf,
            AmountDisclosure.Visible, new Dictionary<Guid, int> { [Dollar] = 2 });
        var figures = customers.Single().Figures;
        Assert.Equal((1, "12.345"), Read(figures.InPortfolioNotDue));
        Assert.Equal((0, "0.00"), Read(figures.Collected));
    }

    private static CustomerInstrumentRiskFact Fact(Guid party, Guid account, Guid currency, CustomerInstrumentStatus status,
        string dueOn, string amount) => new(party, account, currency, (int)status, DateOnly.Parse(dueOn), amount);

    private static (int, string) Read(CustomerInstrumentRiskBucket bucket) => (bucket.Count, bucket.Amount.Money!.Amount);

    private static IEnumerable<CustomerInstrumentRiskBucket> Buckets(CustomerInstrumentRiskTotal total) => Buckets(total.Figures);

    private static IEnumerable<CustomerInstrumentRiskBucket> Buckets(CustomerInstrumentRiskFigures figures) =>
    [
        figures.InPortfolioNotDue, figures.InPortfolioOverdue, figures.AtBankForCollection, figures.Dishonoured,
        figures.ReturnedToCustomer, figures.Collected, figures.OpenExposure,
    ];

    private static IEnumerable<JsonElement> AmountViews(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object when element.TryGetProperty("disclosure", out _) && element.TryGetProperty("money", out _) =>
            [element],
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => AmountViews(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(AmountViews),
        _ => [],
    };
}
