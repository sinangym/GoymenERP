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
/// item of the party's company account, summed per currency as exact decimal text (no conversion, no rounding), LESS the
/// customer's unallocated payments in that currency, net of refunds paid back to the account (SAT-D52; down to zero, never
/// below). No customer named is "Unavailable", never a recorded zero. The admitted open
/// items are approved official invoice installments (<see cref="FinanceOpenItemSourceKinds"/>), so the amounts include taxes. A
/// stored row that does not parse, or a per-currency sum that does not fit an exact <see cref="decimal"/>, makes the figure
/// <see cref="SalesPartyRiskFigureStateV1.Unavailable"/> — never a partial sum. No open item is a recorded zero (an empty list).
/// Customer instruments not yet collected are NOT added: the debt closes at collection (DEC-FIN-016), so the open item still
/// carries it — adding the instrument too would count it twice.</item>
/// <item><b>Credit limit</b> (API-85a, F018 §L1; Finance owns it, SAT-D51; Cari only shows it): the account's ONE approved
/// record of the credit limit master (<see cref="FinancePartyCreditLimitService"/>). An approved Set answers its exact amount in
/// its one currency (<c>IncludesTax</c> null: the notion does not apply to a limit). No approved record, or an approved Remove,
/// is <see cref="SalesPartyRiskFigureStateV1.NoSource"/> (<see cref="CreditLimitNotSet"/>) — never zero, never unlimited. A
/// stored record Finance cannot read exactly (or one naming another party) is <see cref="SalesPartyRiskFigureStateV1.Unavailable"/>.
/// A pending proposal never counts.</item>
/// <item><b>Collateral</b> (API-85b, F018 §L2; FNS-D31, FNS-D57): the account's held received collaterals of the collateral
/// master (<see cref="FinancePartyCollateralService"/>) in force on the read date (the UTC calendar date of the server clock),
/// each weighted by the Settings rate of its type at READ time (amount x rate / 100, exact), summed per currency
/// (<see cref="FinancePartyCollateralFigures"/>; <c>IncludesTax</c> null). No collateral in force is a recorded zero (an
/// empty list). A Settings catalog that cannot answer (the default until Settings registers one), an unknown type, a rate
/// outside 0–100, a stored row Finance cannot read, or a sum System.Decimal cannot hold exactly is
/// <see cref="SalesPartyRiskFigureStateV1.Unavailable"/> — never a partial sum and never an assumed rate. The collateral
/// haircut is OPEN-010 (still open): none is applied. The documentary risk (FNS-D58) is not part of this contract.</item>
/// </list>
/// </summary>
internal sealed class FinanceSalesPartyRiskReadPortV1(FinanceDbContext dbContext, ICompanyContext companyContext,
    IFinanceCollateralTypeCatalogReader collateralTypes, TimeProvider timeProvider)
    : ISalesPartyRiskReadPortV1
{
    /// <summary>Without a Settings collateral type catalog: the collateral figure fails closed when any collateral is held.</summary>
    public FinanceSalesPartyRiskReadPortV1(FinanceDbContext dbContext, ICompanyContext companyContext)
        : this(dbContext, companyContext, new UnavailableFinanceCollateralTypeCatalogReader(), TimeProvider.System)
    {
    }

    /// <summary>The stable source names this owner answers under.</summary>
    public const string OpenItemsSource = "finance.open-items";
    public const string CreditLimitSource = "finance.party-credit-limit";
    public const string CollateralSource = "finance.party-collateral";

    /// <summary>The stable reason codes of this owner's figures.</summary>
    public const string CreditLimitNotSet = "finance_credit_limit_not_set";
    public const string CreditLimitUnreadable = "finance_credit_limit_unreadable";
    public const string OpenItemUnreadable = "finance_open_item_unreadable";
    public const string CustomerUnidentified = "finance_customer_unidentified";

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
        // No customer named: nothing can be read for it — never a recorded zero.
        var receivables = partyId == Guid.Empty || companyAccountId == Guid.Empty
            ? Unavailable(CustomerUnidentified)
            : await OpenReceivablesAsync(companyId, partyId, companyAccountId, cancellationToken);
        var creditLimit = partyId == Guid.Empty || companyAccountId == Guid.Empty
            ? new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Unavailable, CreditLimitSource, [], null, CustomerUnidentified)
            : await CreditLimitAsync(companyId, partyId, companyAccountId, cancellationToken);
        var collateral = partyId == Guid.Empty || companyAccountId == Guid.Empty
            ? new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Unavailable, CollateralSource, [], null, CustomerUnidentified)
            : await CollateralAsync(companyId, partyId, companyAccountId, cancellationToken);
        return new SalesPartyRiskReadAnswerV1(companyId, partyId, companyAccountId, receivables, creditLimit, collateral);
    }

    /// <summary>
    /// API-85b: the weighted collateral of the account — its held received collaterals (effective, not released) in force on
    /// the read date, at the Settings rate of their type read now.
    /// </summary>
    private async Task<SalesPartyRiskFigureV1> CollateralAsync(Guid companyId, Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken)
    {
        var held = await FinancePartyCollateralService.HeldReceivedAsync(dbContext, companyId, companyAccountId,
            cancellationToken);
        if (held.Any(row => row.PartyId != partyId))
        {
            // A collateral of another party on this account: never a guess.
            return new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Unavailable, CollateralSource, [], null,
                FinancePartyCollateralFigures.CollateralUnreadable);
        }

        var asOf = FinancePartyCollateralService.ReadDate(timeProvider);
        var inputs = held.Select(FinancePartyCollateralService.HeldInput).ToList();
        var catalog = inputs.Any(input => FinancePartyCollateralFigures.IsInForce(input.ValidFrom, input.ValidUntil, asOf))
            ? await collateralTypes.ListAsync(companyId, asOf, cancellationToken)
            : null;
        var figure = FinancePartyCollateralFigures.Weigh(inputs, catalog, asOf);
        if (!figure.IsAvailable)
        {
            return new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Unavailable, CollateralSource, [], null,
                figure.ReasonCode);
        }

        var amounts = new List<SalesPartyRiskAmountV1>(figure.Amounts.Count);
        foreach (var amount in figure.Amounts)
        {
            if (!FinancePartyCollateralFigures.TryExactDecimal(amount.Amount, out var value))
            {
                // A weighted sum System.Decimal cannot hold exactly is never rounded to fit.
                return new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Unavailable, CollateralSource, [], null,
                    FinancePartyCollateralFigures.CollateralUnreadable);
            }

            amounts.Add(new SalesPartyRiskAmountV1(amount.CurrencyId, value));
        }

        return new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Available, CollateralSource, amounts,
            IncludesTax: null, null);
    }

    /// <summary>
    /// API-85a: the account's effective credit limit — its one approved record. A proposal (pending, rejected or superseded)
    /// and a superseded approved record never count.
    /// </summary>
    internal async Task<SalesPartyRiskFigureV1> CreditLimitAsync(Guid companyId, Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken)
    {
        const int approved = FinancePartyCreditLimitService.Approved;
        var rows = await dbContext.PartyCreditLimits.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.CompanyAccountId == companyAccountId && row.Status == approved)
            .Select(row => new { row.PartyId, row.Kind, row.CurrencyId, row.AmountText })
            .Take(2)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.NoSource, CreditLimitSource, [], null, CreditLimitNotSet);
        }

        var unreadable = new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Unavailable, CreditLimitSource, [], null,
            CreditLimitUnreadable);
        var effective = rows[0];
        if (rows.Count > 1 || effective.PartyId != partyId)
        {
            // More than one effective record, or a record of another party on this account: never a guess.
            return unreadable;
        }

        if (effective.Kind == FinancePartyCreditLimitService.RemoveKind)
        {
            // An approved Remove: "no limit" — not zero and not unlimited.
            return effective.CurrencyId is null && effective.AmountText is null
                ? new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.NoSource, CreditLimitSource, [], null, CreditLimitNotSet)
                : unreadable;
        }

        if (effective.Kind != FinancePartyCreditLimitService.SetKind || effective.CurrencyId is not { } currencyId
            || currencyId == Guid.Empty || !FinancePartyCreditLimitService.IsExactAmount(effective.AmountText))
        {
            return unreadable;
        }

        var amount = decimal.Parse(effective.AmountText!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Available, CreditLimitSource,
            [new SalesPartyRiskAmountV1(currencyId, amount)], IncludesTax: null, null);
    }

    internal async Task<SalesPartyRiskFigureV1> OpenReceivablesAsync(Guid companyId, Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken)
    {
        var open = FinanceOpenItemStatuses.Open;
        // MOD-15 / ALS-D42: only RECEIVABLE items are the customer's risk. A payable to the same party (it may also be a
        // supplier, DEC-CAR-001) is the company's own debt: it is neither added nor netted off here.
        var receivable = FinanceOpenItemDirectionCodes.Receivable;
        var rows = await dbContext.OpenItems.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.PartyId == partyId && row.CompanyAccountId == companyAccountId
                && row.Status == open && row.Direction == receivable)
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

        // SAT-D52: the customer's payments not yet allocated to an invoice are deducted, per currency.
        if (await UnallocatedPaymentsAsync(companyId, partyId, companyAccountId, cancellationToken) is not { } unallocated)
        {
            return Unavailable();
        }

        foreach (var (currencyId, paid) in unallocated)
        {
            if (sums.TryGetValue(currencyId, out var owed))
            {
                // Open receivables are deducted down to zero, never below: an advance beyond them is not a negative receivable.
                sums[currencyId] = owed.CompareTo(paid) > 0 ? owed.Subtract(paid) : owed.Subtract(owed);
            }
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

    /// <summary>
    /// SAT-D52: per currency, the UNALLOCATED amount of every customer payment Finance recorded for the account — a recorded
    /// bank/POS receipt (<see cref="MoneyEventKinds.ManualBankReceiptRecorded"/>) or a COLLECTED cheque/note
    /// (<see cref="MoneyEventKinds.CustomerInstrumentCollected"/>). A cheque or note not yet collected (and DBS) has no money
    /// event, so it is never counted as paid before the bank pays (DEC-FIN-008, DEC-FIN-016). A source with no allocation state
    /// is wholly unallocated; an allocated one counts its state's unallocated amount; a payment with an APPROVED reversal is
    /// excluded (FIN-T06; its state is also zeroed by the barrier) and the reversal event itself is not a payment. Any row Finance cannot read → null (the caller answers
    /// Unavailable rather than a receivable that is too small or too large). Recorded outgoing payments to the same account
    /// (refunds; <c>OutgoingMoneyEventRow</c>) are netted off the result per currency, down to zero. Allocation itself is not
    /// touched here.
    /// </summary>
    private async Task<SortedDictionary<Guid, FinanceExactDecimalText>?> UnallocatedPaymentsAsync(Guid companyId, Guid partyId,
        Guid companyAccountId, CancellationToken cancellationToken)
    {
        var receipt = MoneyEventKinds.ManualBankReceiptRecorded;
        var collected = MoneyEventKinds.CustomerInstrumentCollected;
        var payments = await dbContext.MoneyEvents.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.PartyId == partyId && row.CompanyAccountId == companyAccountId
                && (row.Kind == receipt || row.Kind == collected))
            .Select(row => new { row.SourceId, row.CurrencyId, row.AmountText })
            .ToListAsync(cancellationToken);
        var sourceIds = payments.Select(row => row.SourceId).ToList();
        var approved = ReversalProposalStatuses.Approved;
        var reversed = sourceIds.Count == 0
            ? new HashSet<Guid>()
            : (await dbContext.ReversalProposals.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.Status == approved && sourceIds.Contains(row.OriginalSourceId))
                .Select(row => row.OriginalSourceId)
                .ToListAsync(cancellationToken)).ToHashSet();
        var states = sourceIds.Count == 0
            ? []
            : await dbContext.SourceAllocationStates.AsNoTracking()
                .Where(row => row.CompanyId == companyId && sourceIds.Contains(row.SourceId))
                .Select(row => new { row.SourceId, row.CurrencyId, row.OriginalAmountText, row.UnallocatedAmountText })
                .ToListAsync(cancellationToken);
        var stateBySource = states.ToDictionary(row => row.SourceId);

        var sums = new SortedDictionary<Guid, FinanceExactDecimalText>();
        foreach (var payment in payments)
        {
            if (payment.SourceId == Guid.Empty || payment.CurrencyId == Guid.Empty
                || !FinanceExactDecimalText.TryParsePositive(payment.AmountText, out var amount))
            {
                return null;
            }

            if (reversed.Contains(payment.SourceId))
            {
                // An approved reversal undid the payment (FIN-T06): it is not a payment any more, allocated or not.
                continue;
            }

            var unallocated = amount;
            if (stateBySource.TryGetValue(payment.SourceId, out var state))
            {
                if (state.CurrencyId != payment.CurrencyId
                    || !FinanceExactDecimalText.Equal(state.OriginalAmountText, payment.AmountText)
                    || !FinanceExactDecimalText.TryParseNonNegative(state.UnallocatedAmountText, out unallocated)
                    || unallocated.CompareTo(amount) > 0)
                {
                    return null;
                }
            }

            sums[payment.CurrencyId] = sums.TryGetValue(payment.CurrencyId, out var sum) ? sum.Add(unallocated) : unallocated;
        }

        // Review A P2-1: a recorded R47 outgoing payment to the same customer account (e.g. an advance refunded) gives that money
        // back, so it is netted off the unallocated credit per currency, down to zero. An outgoing payment to the account that is
        // in fact a supplier payment (a party that is both) can only make the deduction smaller — the receivable larger — never
        // the other way. Any outgoing row Finance cannot read → null (Unavailable), never a partial netting. API-23: an
        // outgoing payment with an approved reversal gave nothing back, so it is not netted.
        var outgoing = await dbContext.OutgoingMoneyEvents.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.PartyId == partyId && row.CompanyAccountId == companyAccountId
                && !dbContext.OutgoingMoneyEventReversals.Any(reversal =>
                    reversal.CompanyId == companyId && reversal.OriginalEventId == row.Id))
            .Select(row => new { row.CurrencyId, row.AmountText })
            .ToListAsync(cancellationToken);
        foreach (var refund in outgoing)
        {
            if (refund.CurrencyId == Guid.Empty || !FinanceExactDecimalText.TryParsePositive(refund.AmountText, out var paidOut))
            {
                return null;
            }

            if (sums.TryGetValue(refund.CurrencyId, out var credit))
            {
                sums[refund.CurrencyId] = credit.CompareTo(paidOut) > 0 ? credit.Subtract(paidOut) : credit.Subtract(credit);
            }
        }

        return sums;
    }

    private static SalesPartyRiskFigureV1 Available(IReadOnlyList<SalesPartyRiskAmountV1> amounts) =>
        new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Available, OpenItemsSource, amounts, IncludesTax: true, null);

    private static SalesPartyRiskFigureV1 Unavailable(string reasonCode = OpenItemUnreadable) =>
        new SalesPartyRiskFigureV1(SalesPartyRiskFigureStateV1.Unavailable, OpenItemsSource, [], null, reasonCode);
}
