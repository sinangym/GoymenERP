using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FIN-SC36 / FNS-D102: the internal treasury ledger, not an external bank statement.
/// Each economic fact is read once: POS uses its bank net, and transfer legs use the
/// one transfer row. Drafts, proposals, allocations, due dates and imported plans do
/// not constitute money. Exact decimal text is never rounded or converted here.
/// </summary>
internal sealed class FinanceTreasuryLedger(FinanceDbContext dbContext, ICompanyContext companyContext)
{
    internal async Task<FinanceTreasuryBalance?> ReadAsync(
        Guid companyId, ExpensePaymentSourceKind kind, Guid accountId, Guid currencyId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId != companyId || companyId == Guid.Empty
            || accountId == Guid.Empty || currencyId == Guid.Empty || !Enum.IsDefined(kind)) return null;

        var accountCurrency = kind == ExpensePaymentSourceKind.Bank
            ? await dbContext.BankAccounts.AsNoTracking().Where(row => row.CompanyId == companyId && row.Id == accountId)
                .Select(row => (Guid?)row.CurrencyId).SingleOrDefaultAsync(cancellationToken)
            : await dbContext.CashAccounts.AsNoTracking().Where(row => row.CompanyId == companyId && row.Id == accountId)
                .Select(row => (Guid?)row.CurrencyId).SingleOrDefaultAsync(cancellationToken);
        if (accountCurrency != currencyId) return null;

        var incoming = new List<string>();
        var outgoing = new List<string>();
        var openingKind = kind == ExpensePaymentSourceKind.Bank
            ? FinanceOpeningBalanceService.BankAccountKind : FinanceOpeningBalanceService.CashAccountKind;
        var openings = await dbContext.OpeningBalanceMovements.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.TargetKind == openingKind && row.TargetId == accountId)
            .Select(row => new { row.Kind, row.CurrencyId, row.Direction, row.AmountText })
            .ToListAsync(cancellationToken);
        foreach (var row in openings)
        {
            if (row.CurrencyId != currencyId || row.Direction is not null) return null;
            if (row.Kind == FinanceOpeningBalanceService.OpeningMovement) incoming.Add(row.AmountText);
            else if (row.Kind == FinanceOpeningBalanceService.ReversalMovement) outgoing.Add(row.AmountText);
            else return null;
        }

        if (kind == ExpensePaymentSourceKind.Bank)
        {
            // Credit plans are not money. Only the approved, explicitly documented actual bank leg is read.
            var credits = await dbContext.CreditMovements.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.BankAccountId == accountId && row.Status == 2)
                .Select(row => new { row.CurrencyId, row.Kind, row.BankAmountText }).ToListAsync(cancellationToken);
            foreach (var row in credits)
            {
                if (row.CurrencyId != currencyId || row.BankAmountText is null) return null;
                if (row.Kind == (int)CreditMovementKindV1.Drawdown) incoming.Add(row.BankAmountText);
                else if (row.Kind == (int)CreditMovementKindV1.Repayment) outgoing.Add(row.BankAmountText);
                else return null;
            }

            var receipts = await dbContext.MoneyEvents.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.TreasuryAccountId == accountId)
                .Select(row => new { row.Kind, row.CurrencyId, row.AmountText, row.PosBankNetAmountText })
                .ToListAsync(cancellationToken);
            foreach (var row in receipts)
            {
                if (row.CurrencyId != currencyId) return null;
                var amount = row.PosBankNetAmountText ?? row.AmountText;
                if (row.Kind is MoneyEventKinds.ManualBankReceiptRecorded or MoneyEventKinds.CustomerInstrumentCollected)
                    incoming.Add(amount);
                else if (row.Kind == MoneyEventKinds.ManualBankReceiptReversed) outgoing.Add(amount);
                else return null;
            }

            var payments = await dbContext.OutgoingMoneyEvents.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.TreasuryAccountId == accountId)
                .Select(row => new { row.CurrencyId, row.AmountText }).ToListAsync(cancellationToken);
            foreach (var row in payments)
            {
                if (row.CurrencyId != currencyId) return null;
                outgoing.Add(row.AmountText);
            }
            var reversals = await dbContext.OutgoingMoneyEventReversals.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.TreasuryAccountId == accountId)
                .Select(row => new { row.CurrencyId, row.AmountText }).ToListAsync(cancellationToken);
            foreach (var row in reversals)
            {
                if (row.CurrencyId != currencyId) return null;
                incoming.Add(row.AmountText);
            }
        }

        var storedKind = kind == ExpensePaymentSourceKind.Bank
            ? ExpensePaymentSourceKinds.Bank : ExpensePaymentSourceKinds.Cash;
        var expenses = await dbContext.ExpensePayments.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.SourceKind == storedKind
                && row.TreasuryAccountId == accountId && row.Status == ExpensePaymentStatuses.Recorded)
            .Select(row => new { row.CurrencyId, row.AmountText }).ToListAsync(cancellationToken);
        foreach (var row in expenses)
        {
            if (row.CurrencyId != currencyId) return null;
            outgoing.Add(row.AmountText);
        }
        var transferKind = kind == ExpensePaymentSourceKind.Bank
            ? CashTransferAccountKinds.Bank : CashTransferAccountKinds.Cash;
        var transfers = await dbContext.CashTransfers.AsNoTracking()
            .Where(row => row.CompanyId == companyId
                && ((row.SourceKind == transferKind && row.SourceAccountId == accountId)
                    || (row.TargetKind == transferKind && row.TargetAccountId == accountId)))
            .Select(row => new { row.SourceKind, row.SourceAccountId, row.SourceCurrencyId, row.SourceAmountText,
                row.TargetKind, row.TargetAccountId, row.TargetCurrencyId, row.TargetAmountText })
            .ToListAsync(cancellationToken);
        foreach (var row in transfers)
        {
            if (row.SourceKind == transferKind && row.SourceAccountId == accountId)
            {
                if (row.SourceCurrencyId != currencyId) return null;
                outgoing.Add(row.SourceAmountText);
            }
            if (row.TargetKind == transferKind && row.TargetAccountId == accountId)
            {
                if (row.TargetCurrencyId != currencyId) return null;
                incoming.Add(row.TargetAmountText);
            }
        }

        return FinanceTreasuryBalance.TryCreate(incoming, outgoing);
    }
}

internal sealed record FinanceTreasuryBalance(FinanceExactDecimalText Incoming, FinanceExactDecimalText Outgoing)
{
    internal bool Covers(FinanceExactDecimalText amount) => Incoming.CompareTo(Outgoing.Add(amount)) >= 0;
    internal bool IsZero => Incoming.CompareTo(Outgoing) == 0;

    internal static FinanceTreasuryBalance? TryCreate(IEnumerable<string> incoming, IEnumerable<string> outgoing)
    {
        var credit = new FinanceExactDecimalText(0, 0);
        var debit = new FinanceExactDecimalText(0, 0);
        foreach (var text in incoming)
        {
            if (!FinanceExactDecimalText.TryParseNonNegative(text, out var value)) return null;
            credit = credit.Add(value);
        }
        foreach (var text in outgoing)
        {
            if (!FinanceExactDecimalText.TryParseNonNegative(text, out var value)) return null;
            debit = debit.Add(value);
        }
        return new FinanceTreasuryBalance(credit, debit);
    }
}

/// <summary>
/// A write guard only inside the caller's SERIALIZABLE Finance transaction. Range locks
/// cover the ledger reads until the same transaction writes the money fact and commits;
/// a read followed by an unrelated SaveChanges transaction is deliberately unavailable.
/// </summary>
public sealed class InternalLedgerTreasuryBalanceGuard(
    FinanceDbContext dbContext, ICompanyContext companyContext) : IFinanceTreasuryBalanceGuard
{
    public async Task<TreasuryBalanceAnswer> CheckOutgoingAsync(
        FinanceContext context, ExpensePaymentSourceKind sourceKind, Guid treasuryAccountId,
        MoneyText money, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (money is null || !dbContext.Database.IsRelational()
            || dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || !FinanceExactDecimalText.TryParseNonNegative(money.Amount, out var amount))
            return new TreasuryBalanceAnswer(TreasuryBalanceOutcome.Unavailable, TreasuryBalanceAnswer.UnavailableCode);
        var balance = await new FinanceTreasuryLedger(dbContext, companyContext)
            .ReadAsync(context.CompanyId, sourceKind, treasuryAccountId, money.CurrencyId, cancellationToken);
        return balance is null
            ? new TreasuryBalanceAnswer(TreasuryBalanceOutcome.Unavailable, TreasuryBalanceAnswer.UnavailableCode)
            : balance.Covers(amount)
                ? new TreasuryBalanceAnswer(TreasuryBalanceOutcome.Sufficient, null)
                : new TreasuryBalanceAnswer(TreasuryBalanceOutcome.Insufficient, TreasuryBalanceAnswer.InsufficientCode);
    }
}
