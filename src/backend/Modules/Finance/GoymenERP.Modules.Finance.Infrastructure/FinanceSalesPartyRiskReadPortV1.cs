using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// MOD-01: the Finance-owned answer to the Sales-declared <see cref="ISalesPartyRiskReadPortV1"/> — one customer account's risk
/// inputs in the server-resolved company, from Finance's OWN store only (the Logistics AT-024 port shape). Untracked; it creates
/// nothing (no attempt, audit or version effect) and carries no authority: the Sales risk read measures its own name around it.
/// <list type="bullet">
/// <item><b>Open receivables</b> (DEC-FIN-005 / DEC-FIN-007 "açık alacak"): the OUTSTANDING amount of every OPEN Finance open
/// item of the party's company account, summed per currency as exact decimal text (no conversion, no rounding). The admitted open
/// items are approved official invoice installments (<see cref="FinanceOpenItemSourceKinds"/>), so the amounts include taxes. A
/// stored row that does not parse, or a per-currency sum that does not fit an exact <see cref="decimal"/>, makes the figure
/// <see cref="SalesPartyRiskFigureStateV1.Unavailable"/> — never a partial sum. No open item is a recorded zero (an empty list).
/// Customer instruments not yet collected are NOT added: the debt closes at collection (DEC-FIN-016), so the open item still
/// carries it — adding the instrument too would count it twice.</item>
/// <item><b>Credit limit</b> and <b>collateral</b>: no Finance or Cari master of either exists on main (W4-RISK; the collateral
/// haircut is OPEN-010, still open). Both are <see cref="SalesPartyRiskFigureStateV1.NoSource"/> — never zero, never unlimited.</item>
/// </list>
/// </summary>
internal sealed class FinanceSalesPartyRiskReadPortV1(FinanceDbContext dbContext, ICompanyContext companyContext)
    : ISalesPartyRiskReadPortV1
{
    /// <summary>The stable source names this owner answers under.</summary>
    public const string OpenItemsSource = "finance.open-items";
    public const string CreditLimitSource = "finance.party-credit-limit";
    public const string CollateralSource = "finance.party-collateral";

    /// <summary>The stable reason codes of this owner's figures.</summary>
    public const string CreditLimitOwnerMissing = "finance_credit_limit_owner_missing";
    public const string CollateralOwnerMissing = "finance_collateral_owner_missing";
    public const string OpenItemUnreadable = "finance_open_item_unreadable";

    public async Task<SalesPartyRiskReadAnswerV1> ReadPartyRiskAsync(Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Cari risk okuması yalnız çözümlenmiş şirket bağlamında yapılır.");
        }

        var companyId = companyContext.CompanyId;
        var receivables = partyId == Guid.Empty || companyAccountId == Guid.Empty
            ? Available([])
            : await OpenReceivablesAsync(companyId, partyId, companyAccountId, cancellationToken);
        return new SalesPartyRiskReadAnswerV1(companyId, partyId, companyAccountId, receivables,
            new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.NoSource, CreditLimitSource, [], null, CreditLimitOwnerMissing),
            new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.NoSource, CollateralSource, [], null, CollateralOwnerMissing));
    }

    private async Task<SalesPartyRiskFigureV1> OpenReceivablesAsync(Guid companyId, Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken)
    {
        var open = FinanceOpenItemStatuses.Open;
        var rows = await dbContext.OpenItems.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.PartyId == partyId && row.CompanyAccountId == companyAccountId
                && row.Status == open)
            .Select(row => new { row.CurrencyId, row.SourceKind, row.OutstandingAmountText })
            .ToListAsync(cancellationToken);

        var sums = new SortedDictionary<Guid, FinanceExactDecimalText>();
        foreach (var row in rows)
        {
            if (row.CurrencyId == Guid.Empty
                || !string.Equals(row.SourceKind, FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment, StringComparison.Ordinal)
                || !FinanceExactDecimalText.TryParseNonNegative(row.OutstandingAmountText, out var outstanding))
            {
                // A stored row Finance cannot read (or an open-item source whose tax basis is not known here): no partial sum.
                return Unavailable();
            }

            sums[row.CurrencyId] = sums.TryGetValue(row.CurrencyId, out var sum) ? sum.Add(outstanding) : outstanding;
        }

        var amounts = new List<SalesPartyRiskAmountV1>(sums.Count);
        foreach (var (currencyId, sum) in sums)
        {
            var text = sum.Text;
            if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
                || !FinanceExactDecimalText.Equal(text, amount.ToString(CultureInfo.InvariantCulture)))
            {
                // Finance keeps amounts without a digit limit; a sum System.Decimal cannot hold exactly is never rounded to fit.
                return Unavailable();
            }

            amounts.Add(new SalesPartyRiskAmountV1(currencyId, amount));
        }

        return Available(amounts);
    }

    private static SalesPartyRiskFigureV1 Available(IReadOnlyList<SalesPartyRiskAmountV1> amounts) =>
        new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Available, OpenItemsSource, amounts, IncludesTax: true, null);

    private static SalesPartyRiskFigureV1 Unavailable() =>
        new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Unavailable, OpenItemsSource, [], null, OpenItemUnreadable);
}
