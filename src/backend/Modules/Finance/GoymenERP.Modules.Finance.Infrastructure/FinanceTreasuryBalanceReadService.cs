using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// AI-D43 (WP-A3c): the authorized ledger balance of one treasury account. The balance is the internal Finance treasury
/// ledger's (<see cref="FinanceTreasuryLedger"/>, FIN-SC36 / FNS-D102: each economic fact once, POS at its bank net,
/// transfers from the one transfer row) — not a bank statement. <c>finance.treasury-balance.read</c> is measured for the
/// server's actor and company before and after the read; the account is looked up in the context company only. Reads are
/// no-tracking and never save. A ledger the owner cannot tell is Unavailable, never a zero.
/// </summary>
public sealed class FinanceTreasuryBalanceReadService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions) : IFinanceTreasuryBalanceReadV1
{
    public async Task<ReadResult<FinanceTreasuryBalanceViewV1>> ReadAuthorizedAsync(
        FinanceContext context,
        FinanceTreasuryAccountKindV1 kind,
        Guid accountId,
        DateOnly? throughDate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return new(ReadOutcome.Denied, null);
        }

        var before = await permissions.CheckAsync(context, FinanceAction.ReadTreasuryBalances, accountId, cancellationToken);
        if (before != PermissionDecision.Allowed || !SameContext(context))
        {
            return new(Refusal(before), null);
        }

        if (accountId == Guid.Empty || !Enum.IsDefined(kind))
        {
            return new(ReadOutcome.NotFound, null);
        }

        var currencyId = kind == FinanceTreasuryAccountKindV1.Bank
            ? await dbContext.BankAccounts.AsNoTracking().Where(row => row.CompanyId == context.CompanyId && row.Id == accountId)
                .Select(row => (Guid?)row.CurrencyId).SingleOrDefaultAsync(cancellationToken)
            : await dbContext.CashAccounts.AsNoTracking().Where(row => row.CompanyId == context.CompanyId && row.Id == accountId)
                .Select(row => (Guid?)row.CurrencyId).SingleOrDefaultAsync(cancellationToken);
        if (currencyId is not { } currency || currency == Guid.Empty)
        {
            return new(ReadOutcome.NotFound, null);
        }

        var ledgerKind = kind == FinanceTreasuryAccountKindV1.Bank ? ExpensePaymentSourceKind.Bank : ExpensePaymentSourceKind.Cash;
        var balance = await new FinanceTreasuryLedger(dbContext, companyContext)
            .ReadAsync(context.CompanyId, ledgerKind, accountId, currency, cancellationToken, throughDate);

        var after = await permissions.CheckAsync(context, FinanceAction.ReadTreasuryBalances, accountId, cancellationToken);
        if (after != PermissionDecision.Allowed || !SameContext(context))
        {
            return new(Refusal(after), null);
        }

        return balance is null
            ? new(ReadOutcome.Unavailable, null)
            : new(ReadOutcome.Found, new FinanceTreasuryBalanceViewV1(context.CompanyId, kind, accountId, currency, throughDate,
                SignedText(balance)));
    }

    /// <summary>Incoming minus outgoing as exact text; a ledger whose outgoing exceeds its incoming keeps its sign.</summary>
    internal static string SignedText(FinanceTreasuryBalance balance) =>
        balance.Incoming.CompareTo(balance.Outgoing) >= 0
            ? balance.Incoming.Subtract(balance.Outgoing).Text
            : "-" + balance.Outgoing.Subtract(balance.Incoming).Text;

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId
        && currentUser.CanAccessCompany(context.CompanyId);

    private static ReadOutcome Refusal(PermissionDecision decision) =>
        decision == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied;
}
