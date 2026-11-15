namespace GoymenERP.Modules.Finance.Contracts;

// B-05 / M-03 (FNS-D14, KLN-D3): the party-account read port that Muhasebe > Cari Hesaplar and the Cari 360 finance tab
// consume. It reads Finance's ONE open-item sub-ledger and the recorded facts the party ledger already reads; it opens
// no new master, writes nothing and converts or adds no currency (OPEN-012). A party account is one party at one
// company account (FNS-D6), so every request names both.

/// <summary>One party account of the context company.</summary>
public sealed record PartyAccountRequestV1(Guid PartyId, Guid CompanyAccountId);

/// <summary>
/// One still-open receivable or payable item of the account, as Finance admitted it. An item is listed only while its
/// stored status is open; the status follows the outstanding amount, so items are disclosed only with the amount
/// authority (the open-item read FI04 keeps status and amounts together for the same reason).
/// </summary>
public sealed record PartyAccountOpenItemV1(
    Guid OpenItemId,
    FinanceOpenItemDirectionV1 Direction,
    Guid SourceId,
    Guid CurrencyId,
    DateOnly? DueOn,
    MoneyText OriginalAmount,
    MoneyText OutstandingAmount);

/// <summary>
/// The account's all-time balance summary per currency (the API-04 summary, unchanged) and its open items. When the
/// amount authority is not granted, <see cref="OpenItems"/> is empty and <see cref="OpenItemsDisclosure"/> says why
/// (<c>Redacted</c> or <c>Unavailable</c>); an empty list with <c>Visible</c> means the account has no open item.
/// </summary>
public sealed record PartyAccountViewV1(
    Guid CompanyId,
    Guid PartyId,
    Guid CompanyAccountId,
    IReadOnlyList<PartyBalanceCurrency> Currencies,
    AmountDisclosure OpenItemsDisclosure,
    IReadOnlyList<PartyAccountOpenItemV1> OpenItems);

public enum PartyAccountReadOutcome
{
    Unavailable,
    Denied,
    Invalid,
    Found,
    /// <summary>The all-time summary reads more facts than <see cref="PartyBalanceRules.MaximumFacts"/>.</summary>
    TooManyFacts,
    /// <summary>The account has more open items than <see cref="PartyAccountReadRules.MaximumAccountOpenItems"/>.</summary>
    TooManyOpenItems,
}

public sealed record PartyAccountReadResultV1(PartyAccountReadOutcome Outcome, PartyAccountViewV1? Account);

/// <summary>
/// One currency of one party account in the Cari Hesaplar list: the count and the exact sum of its still-open
/// receivable and payable items. It is "open receivable / open payable", never a balance: a balance needs every
/// recorded fact (the party balance read), not only the open items.
/// </summary>
public sealed record PartyAccountOpenCurrencyV1(
    Guid CurrencyId,
    int ReceivableCount,
    MoneyText OpenReceivable,
    int PayableCount,
    MoneyText OpenPayable);

/// <summary>One party account that has at least one open item, with one block per currency.</summary>
public sealed record PartyAccountSummaryV1(
    Guid PartyId,
    Guid CompanyAccountId,
    IReadOnlyList<PartyAccountOpenCurrencyV1> Currencies);

/// <summary>The context company's party accounts with open items, ordered by party then company account.</summary>
public sealed record PartyAccountListV1(Guid CompanyId, IReadOnlyList<PartyAccountSummaryV1> Accounts);

public enum PartyAccountListOutcome
{
    Unavailable,
    Denied,
    Found,
    /// <summary>The company has more open items than <see cref="PartyAccountReadRules.MaximumOpenItems"/>.</summary>
    TooManyOpenItems,
}

public sealed record PartyAccountListResultV1(PartyAccountListOutcome Outcome, PartyAccountListV1? List);

public static class PartyAccountReadRules
{
    /// <summary>
    /// ASSUMPTION-FIN-S11-CAP (technical, reversible; the same shape as <see cref="PartyBalanceRules.MaximumFacts"/>):
    /// amounts are stored as exact text, so the list sums in memory and reads at most this many open items. Beyond it
    /// the list is refused, never cut, because a cut list would show wrong open totals.
    /// </summary>
    public const int MaximumOpenItems = 20000;

    /// <summary>ASSUMPTION-FIN-S11-CAP: the open items one account's view lists; beyond it the view is refused.</summary>
    public const int MaximumAccountOpenItems = 1000;

    public const string TooManyOpenItemsCode = "finance_party_accounts_too_many_open_items";
    public const string TooManyAccountOpenItemsCode = "finance_party_account_too_many_open_items";
}

/// <summary>
/// The B-05 party-account read port. Both reads are behind the party ledger's authorities: <c>ReadPartyLedger</c>
/// measured before and after the reads, <c>ReadPartyLedgerAmount</c> for every amount. The list exists only to show
/// open amounts, so it is refused (Denied / Unavailable) without the amount authority rather than shown hollow.
/// </summary>
public interface IPartyLedgerReadServiceV1
{
    Task<PartyAccountListResultV1> ListAuthorizedAsync(FinanceContext context, CancellationToken cancellationToken);

    Task<PartyAccountReadResultV1> GetAuthorizedAsync(FinanceContext context, PartyAccountRequestV1 request,
        CancellationToken cancellationToken);
}
