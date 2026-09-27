using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>The facts of one received instrument the risk view needs (a projection; no amount leaves unless disclosed).</summary>
internal sealed record CustomerInstrumentRiskFact(
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    int Status,
    DateOnly DueOn,
    string AmountText);

/// <summary>
/// DEC-FIN-016 risk tracking (user decision 2026-09-27), pure: buckets received instruments by risk state as of a date,
/// per customer account and currency, and totals the returned lines per currency. Sums are exact decimal text (no
/// floating point, no rounding, no local digit limit); amounts of different currencies are never added. Facts only:
/// no limit, threshold or warning (OPEN-010).
/// </summary>
internal static class CustomerInstrumentRiskPolicy
{
    public static (IReadOnlyList<CustomerInstrumentRiskLine> Customers, IReadOnlyList<CustomerInstrumentRiskTotal> Totals)
        Build(IEnumerable<CustomerInstrumentRiskFact> facts, DateOnly asOf, AmountDisclosure disclosure)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var lines = facts
            .GroupBy(fact => (fact.PartyId, fact.CompanyAccountId, fact.CurrencyId))
            .OrderBy(group => group.Key.PartyId).ThenBy(group => group.Key.CompanyAccountId).ThenBy(group => group.Key.CurrencyId)
            .Select(group => (group.Key, Tally: Tally.Of(group, asOf)))
            .ToList();
        var totals = lines
            .GroupBy(line => line.Key.CurrencyId)
            .OrderBy(group => group.Key)
            .Select(group => new CustomerInstrumentRiskTotal(group.Key,
                group.Select(line => line.Tally).Aggregate(Tally.Empty, Tally.Plus).Figures(group.Key, disclosure)))
            .ToList();
        return (
            [.. lines.Select(line => new CustomerInstrumentRiskLine(line.Key.PartyId, line.Key.CompanyAccountId,
                line.Key.CurrencyId, line.Tally.Figures(line.Key.CurrencyId, disclosure)))],
            totals);
    }

    private readonly record struct Sum(int Count, FinanceExactDecimalText Amount)
    {
        public static Sum Zero => new(0, new FinanceExactDecimalText(0, 0));

        public Sum Plus(Sum other) => new(checked(Count + other.Count), Amount.Add(other.Amount));

        public CustomerInstrumentRiskBucket Bucket(Guid currencyId, AmountDisclosure disclosure) => new(Count,
            disclosure == AmountDisclosure.Visible
                ? new AmountView(AmountDisclosure.Visible, new MoneyText(Amount.Text, currencyId))
                : new AmountView(disclosure, null));
    }

    private sealed record Tally(IReadOnlyDictionary<CustomerInstrumentRiskState, Sum> ByState)
    {
        public static Tally Empty { get; } = new(new Dictionary<CustomerInstrumentRiskState, Sum>());

        public static Tally Of(IEnumerable<CustomerInstrumentRiskFact> facts, DateOnly asOf)
        {
            var byState = new Dictionary<CustomerInstrumentRiskState, Sum>();
            foreach (var fact in facts)
            {
                // A stored face value passed the profile's exact scale; anything else is a broken invariant, not a 0.
                if (!FinanceExactDecimalText.TryParsePositive(fact.AmountText, out var amount))
                {
                    throw new InvalidOperationException("Müşteri çeki/senedi tutarı okunamadı; risk görünümü hesaplanmaz.");
                }

                var state = CustomerInstrumentRisk.StateOf((CustomerInstrumentStatus)fact.Status, fact.DueOn, asOf);
                byState[state] = Get(byState, state).Plus(new Sum(1, amount));
            }

            return new Tally(byState);
        }

        public static Tally Plus(Tally left, Tally right)
        {
            var byState = new Dictionary<CustomerInstrumentRiskState, Sum>(left.ByState);
            foreach (var (state, sum) in right.ByState)
            {
                byState[state] = Get(byState, state).Plus(sum);
            }

            return new Tally(byState);
        }

        public CustomerInstrumentRiskFigures Figures(Guid currencyId, AmountDisclosure disclosure)
        {
            CustomerInstrumentRiskBucket Of(CustomerInstrumentRiskState state) =>
                Get(ByState, state).Bucket(currencyId, disclosure);
            var exposure = ByState.Where(pair => CustomerInstrumentRisk.IsOpenExposure(pair.Key))
                .Select(pair => pair.Value)
                .Aggregate(Sum.Zero, (sum, next) => sum.Plus(next));
            return new CustomerInstrumentRiskFigures(
                Of(CustomerInstrumentRiskState.InPortfolioNotDue),
                Of(CustomerInstrumentRiskState.InPortfolioOverdue),
                Of(CustomerInstrumentRiskState.AtBankForCollection),
                Of(CustomerInstrumentRiskState.Dishonoured),
                Of(CustomerInstrumentRiskState.ReturnedToCustomer),
                Of(CustomerInstrumentRiskState.Collected),
                exposure.Bucket(currencyId, disclosure));
        }

        private static Sum Get(IReadOnlyDictionary<CustomerInstrumentRiskState, Sum> byState, CustomerInstrumentRiskState state) =>
            byState.TryGetValue(state, out var sum) ? sum : Sum.Zero;
    }
}
