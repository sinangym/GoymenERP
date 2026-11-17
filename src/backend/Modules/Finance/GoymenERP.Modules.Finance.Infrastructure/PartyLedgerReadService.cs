using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// B-05 / M-03 (FNS-D14, KLN-D3): the party-account read port over Finance's one open-item sub-ledger, read from the
/// tenant-filtered <see cref="FinanceDbContext"/> only (the context company's rows; no raw SQL, no filter bypass, no
/// write). The account view reuses the API-04 summary (<see cref="IPartyLedgerService.SummarizeAuthorizedAsync"/>) as
/// it is and adds the account's open items; the list sums each party account's open items per currency. Nothing is
/// converted or added across currencies (OPEN-012), nothing is carried forward (OPEN-018).
/// <para>
/// Authority is the party ledger's: <c>ReadPartyLedger</c> before anything is read and again after the reads, and
/// <c>ReadPartyLedgerAmount</c> for every amount. An open item's status follows its outstanding amount, so open items
/// (and the list, which exists only to show open amounts) are disclosed only with the amount authority. A stored row
/// that breaks an invariant (an unknown direction, an amount that does not parse) withholds the answer as unavailable
/// rather than giving a partial or wrong sum.
/// </para>
/// </summary>
public sealed class PartyLedgerReadService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    IPartyLedgerService ledger) : IPartyLedgerReadServiceV1
{
    public async Task<PartyAccountListResultV1> ListAuthorizedAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return ListRefused(PartyAccountListOutcome.Denied);
        }

        var read = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedger, null, cancellationToken);
        if (read != PermissionDecision.Allowed || !SameContext(context))
        {
            return ListRefused(ListRefusal(read));
        }

        var amount = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedgerAmount, null, cancellationToken);
        if (amount != PermissionDecision.Allowed || !SameContext(context))
        {
            return ListRefused(ListRefusal(amount));
        }

        var open = FinanceOpenItemStatuses.Open;
        var originals = await dbContext.OpenItems.AsNoTracking()
            .Where(row => row.Status == open)
            .OrderBy(row => row.PartyId).ThenBy(row => row.CompanyAccountId).ThenBy(row => row.Id)
            .Take(PartyAccountReadRules.MaximumOpenItems + 1).ToListAsync(cancellationToken);
        IReadOnlyList<FinanceOpenItemPosition> positions;
        try { positions = await FinancePartyBalanceAssignment.OpenPositionsAsync(dbContext, originals, cancellationToken); }
        catch (InvalidOperationException) { return ListRefused(PartyAccountListOutcome.Unavailable); }
        var rows = positions.Select(x => new OpenItemSum(x.Balance.PartyId, x.Balance.CompanyAccountId,
            x.Source.CurrencyId, x.Source.Direction, x.Balance.AvailableAmount)).ToList();

        // Both authorities are measured afresh after the read: a revoked actor never receives what was read.
        var again = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedger, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
        {
            return ListRefused(ListRefusal(again));
        }

        var amountAgain = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedgerAmount, null,
            cancellationToken);
        if (amountAgain != PermissionDecision.Allowed || !SameContext(context))
        {
            return ListRefused(ListRefusal(amountAgain));
        }

        if (originals.Count > PartyAccountReadRules.MaximumOpenItems || rows.Count > PartyAccountReadRules.MaximumOpenItems)
        {
            return ListRefused(PartyAccountListOutcome.TooManyOpenItems);
        }

        var accounts = new List<PartyAccountSummaryV1>();
        foreach (var account in rows.GroupBy(row => (row.PartyId, row.CompanyAccountId)))
        {
            var currencies = new List<PartyAccountOpenCurrencyV1>();
            foreach (var currency in account.GroupBy(row => row.CurrencyId).OrderBy(group => group.Key))
            {
                var receivable = Zero;
                var payable = Zero;
                var receivableCount = 0;
                var payableCount = 0;
                foreach (var row in currency)
                {
                    if (!FinanceExactDecimalText.TryParseNonNegative(row.OutstandingAmountText, out var outstanding))
                    {
                        return ListRefused(PartyAccountListOutcome.Unavailable);
                    }

                    switch (row.Direction)
                    {
                        case FinanceOpenItemDirectionCodes.Receivable:
                            receivable = receivable.Add(outstanding);
                            receivableCount++;
                            break;
                        case FinanceOpenItemDirectionCodes.Payable:
                            payable = payable.Add(outstanding);
                            payableCount++;
                            break;
                        default:
                            return ListRefused(PartyAccountListOutcome.Unavailable);
                    }
                }

                currencies.Add(new PartyAccountOpenCurrencyV1(currency.Key, receivableCount,
                    new MoneyText(receivable.Text, currency.Key), payableCount, new MoneyText(payable.Text, currency.Key)));
            }

            accounts.Add(new PartyAccountSummaryV1(account.Key.PartyId, account.Key.CompanyAccountId, currencies));
        }

        return new PartyAccountListResultV1(PartyAccountListOutcome.Found,
            new PartyAccountListV1(context.CompanyId, accounts));
    }

    public async Task<PartyAccountReadResultV1> GetAuthorizedAsync(FinanceContext context,
        PartyAccountRequestV1 request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return Refused(PartyAccountReadOutcome.Denied);
        }

        // The API-04 summary measures its own fence (ReadPartyLedger before and after, the amount for every amount).
        var summary = await ledger.SummarizeAuthorizedAsync(context,
            new PartyBalanceRequest(request.PartyId, request.CompanyAccountId), cancellationToken);
        switch (summary.Outcome)
        {
            case PartyBalanceOutcome.Found when summary.Balance is not null:
                break;
            case PartyBalanceOutcome.Denied:
                return Refused(PartyAccountReadOutcome.Denied);
            case PartyBalanceOutcome.Invalid:
                return Refused(PartyAccountReadOutcome.Invalid);
            case PartyBalanceOutcome.TooManyFacts:
                return Refused(PartyAccountReadOutcome.TooManyFacts);
            default:
                return Refused(PartyAccountReadOutcome.Unavailable);
        }

        var partyId = request.PartyId;
        var accountId = request.CompanyAccountId;
        var open = FinanceOpenItemStatuses.Open;
        var originals = await FinancePartyBalanceAssignment.OpenItemsOf(dbContext, partyId, accountId).AsNoTracking()
            .Where(row => row.Status == open)
            .OrderBy(row => row.DueOn == null).ThenBy(row => row.DueOn).ThenBy(row => row.CreatedAtUtc)
            .ThenBy(row => row.Id).Take(PartyAccountReadRules.MaximumAccountOpenItems + 1)
            .ToListAsync(cancellationToken);
        IReadOnlyList<FinanceOpenItemPosition> positions;
        try { positions = await FinancePartyBalanceAssignment.OpenPositionsAsync(dbContext, originals, cancellationToken); }
        catch (InvalidOperationException) { return Refused(PartyAccountReadOutcome.Unavailable); }
        var rows = positions.Where(x => x.Balance.PartyId == partyId && x.Balance.CompanyAccountId == accountId)
            .Select(x => new OpenItemDetail(x.Source.Id, x.Source.Direction, x.Source.SourceId, x.Source.CurrencyId,
                x.Source.DueOn, x.Source.OriginalAmountText, x.Balance.AvailableAmount)).ToList();

        var amount = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedgerAmount, null, cancellationToken);
        var again = await permissions.CheckAsync(context, FinanceAction.ReadPartyLedger, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
        {
            return Refused(ReadRefusal(again));
        }

        var disclosure = Disclosure(amount);
        // The summary's amounts were disclosed under its own measurement; a view whose two halves disagree (the
        // authority changed between them) is not this contract's, so it is withheld rather than shown mixed.
        if (summary.Balance.Currencies.Any(currency => currency.DebitTotal.Disclosure != disclosure))
        {
            return Refused(PartyAccountReadOutcome.Unavailable);
        }

        if (disclosure != AmountDisclosure.Visible)
        {
            return Found(context, summary.Balance, disclosure, []);
        }

        if (originals.Count > PartyAccountReadRules.MaximumAccountOpenItems || rows.Count > PartyAccountReadRules.MaximumAccountOpenItems)
        {
            return Refused(PartyAccountReadOutcome.TooManyOpenItems);
        }

        var items = new List<PartyAccountOpenItemV1>(rows.Count);
        foreach (var row in rows)
        {
            var direction = row.Direction switch
            {
                FinanceOpenItemDirectionCodes.Receivable => FinanceOpenItemDirectionV1.Receivable,
                FinanceOpenItemDirectionCodes.Payable => FinanceOpenItemDirectionV1.Payable,
                _ => (FinanceOpenItemDirectionV1?)null,
            };
            if (direction is null
                || !FinanceExactDecimalText.TryParseNonNegative(row.OriginalAmountText, out var original)
                || !FinanceExactDecimalText.TryParseNonNegative(row.OutstandingAmountText, out var outstanding))
            {
                return Refused(PartyAccountReadOutcome.Unavailable);
            }

            items.Add(new PartyAccountOpenItemV1(row.Id, direction.Value, row.SourceId, row.CurrencyId, row.DueOn,
                new MoneyText(original.Text, row.CurrencyId), new MoneyText(outstanding.Text, row.CurrencyId)));
        }

        return Found(context, summary.Balance, disclosure, items);
    }

    private static readonly FinanceExactDecimalText Zero = new(0, 0);

    private static PartyAccountReadResultV1 Found(FinanceContext context, PartyBalanceView balance,
        AmountDisclosure disclosure, IReadOnlyList<PartyAccountOpenItemV1> items) =>
        new(PartyAccountReadOutcome.Found, new PartyAccountViewV1(context.CompanyId, balance.PartyId,
            balance.CompanyAccountId, balance.Currencies, disclosure, items));

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private static AmountDisclosure Disclosure(PermissionDecision decision) => decision switch
    {
        PermissionDecision.Allowed => AmountDisclosure.Visible,
        PermissionDecision.Denied => AmountDisclosure.Redacted,
        _ => AmountDisclosure.Unavailable,
    };

    private static PartyAccountReadResultV1 Refused(PartyAccountReadOutcome outcome) => new(outcome, null);

    private static PartyAccountReadOutcome ReadRefusal(PermissionDecision decision) =>
        decision == PermissionDecision.Unavailable ? PartyAccountReadOutcome.Unavailable : PartyAccountReadOutcome.Denied;

    private static PartyAccountListResultV1 ListRefused(PartyAccountListOutcome outcome) => new(outcome, null);

    private static PartyAccountListOutcome ListRefusal(PermissionDecision decision) =>
        decision == PermissionDecision.Unavailable ? PartyAccountListOutcome.Unavailable : PartyAccountListOutcome.Denied;

    private sealed record OpenItemSum(Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, int Direction,
        string OutstandingAmountText);

    private sealed record OpenItemDetail(Guid Id, int Direction, Guid SourceId, Guid CurrencyId, DateOnly? DueOn,
        string OriginalAmountText, string OutstandingAmountText);
}
