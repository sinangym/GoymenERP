using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// The facts of one received instrument the risk view needs, with its status AS OF the view's day (the status of its
/// last movement on or before that day); a projection — no amount leaves unless disclosed.
/// </summary>
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
    /// <param name="scales">
    /// The display scale per currency (P3-1): every sum, an empty bucket's zero included, is written at it ("0.00"),
    /// never with fewer digits than the sum itself carries.
    /// </param>
    public static (IReadOnlyList<CustomerInstrumentRiskLine> Customers, IReadOnlyList<CustomerInstrumentRiskTotal> Totals)
        Build(IEnumerable<CustomerInstrumentRiskFact> facts, DateOnly asOf, AmountDisclosure disclosure,
            IReadOnlyDictionary<Guid, int> scales)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(scales);
        int ScaleOf(Guid currencyId) => scales.TryGetValue(currencyId, out var scale) ? scale : 0;
        var lines = facts
            .GroupBy(fact => (fact.PartyId, fact.CompanyAccountId, fact.CurrencyId))
            .OrderBy(group => group.Key.PartyId).ThenBy(group => group.Key.CompanyAccountId).ThenBy(group => group.Key.CurrencyId)
            .Select(group => (group.Key, Tally: Tally.Of(group, asOf)))
            .ToList();
        var totals = lines
            .GroupBy(line => line.Key.CurrencyId)
            .OrderBy(group => group.Key)
            .Select(group => new CustomerInstrumentRiskTotal(group.Key,
                group.Select(line => line.Tally).Aggregate(Tally.Empty, Tally.Plus)
                    .Figures(group.Key, ScaleOf(group.Key), disclosure)))
            .ToList();
        return (
            [.. lines.Select(line => new CustomerInstrumentRiskLine(line.Key.PartyId, line.Key.CompanyAccountId,
                line.Key.CurrencyId, line.Tally.Figures(line.Key.CurrencyId, ScaleOf(line.Key.CurrencyId), disclosure)))],
            totals);
    }

    private readonly record struct Sum(int Count, FinanceExactDecimalText Amount)
    {
        public static Sum Zero => new(0, new FinanceExactDecimalText(0, 0));

        public Sum Plus(Sum other) => new(checked(Count + other.Count), Amount.Add(other.Amount));

        public CustomerInstrumentRiskBucket Bucket(Guid currencyId, int scale, AmountDisclosure disclosure) => new(Count,
            disclosure == AmountDisclosure.Visible
                // Adding a zero at the currency scale widens the text to it and never cuts a digit.
                ? new AmountView(AmountDisclosure.Visible,
                    new MoneyText(Amount.Add(new FinanceExactDecimalText(0, scale)).Text, currencyId))
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

        public CustomerInstrumentRiskFigures Figures(Guid currencyId, int scale, AmountDisclosure disclosure)
        {
            CustomerInstrumentRiskBucket Of(CustomerInstrumentRiskState state) =>
                Get(ByState, state).Bucket(currencyId, scale, disclosure);
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
                exposure.Bucket(currencyId, scale, disclosure));
        }

        private static Sum Get(IReadOnlyDictionary<CustomerInstrumentRiskState, Sum> byState, CustomerInstrumentRiskState state) =>
            byState.TryGetValue(state, out var sum) ? sum : Sum.Zero;
    }
}
