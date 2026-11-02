namespace GoymenERP.Modules.Finance.Contracts;

// F1b / API-04 (FNS-D6, FNS-D7): one party account's balance summary — hubvogal's "Toplam Borç / Toplam Alacak /
// Bakiye" — per currency, never converted or added across currencies (AT-011). It is the party ledger's own rule over
// ALL of the account's recorded facts (no date range), plus the open receivable and open payable that are still
// outstanding. A display read: nothing is netted for risk (ALS-D42) and nothing is written.

/// <summary>One party account of the context company.</summary>
public sealed record PartyBalanceRequest(Guid PartyId, Guid CompanyAccountId);

/// <summary>
/// One currency. <paramref name="DebitTotal"/> / <paramref name="CreditTotal"/> / <paramref name="Balance"/> are the
/// ledger's totals over every recorded fact (borç / alacak / bakiye, from the company's view of the party's account).
/// <paramref name="OpenReceivable"/> is what the party still owes on OPEN receivable items and
/// <paramref name="OpenPayable"/> what the company still owes on OPEN payable items (MOD-15). All amounts and the
/// balance side are disclosed only with the ledger amount grant.
/// </summary>
public sealed record PartyBalanceCurrency(
    Guid CurrencyId,
    AmountView DebitTotal,
    AmountView CreditTotal,
    PartyLedgerBalanceView Balance,
    AmountView OpenReceivable,
    AmountView OpenPayable);

public sealed record PartyBalanceView(
    Guid CompanyId,
    Guid PartyId,
    Guid CompanyAccountId,
    IReadOnlyList<PartyBalanceCurrency> Currencies);

public enum PartyBalanceOutcome
{
    Unavailable,
    Denied,

    /// <summary>A malformed request: an empty id.</summary>
    Invalid,
    Found,
}

public sealed record PartyBalanceResult(PartyBalanceOutcome Outcome, PartyBalanceView? Balance);
