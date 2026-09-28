using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// First-release customer / party ledger ("cari hesap ekstresi"). A READ over Finance's own recorded facts only — the
// money events of recorded receipts, their reversals and cheque / note collections, the outgoing money events of
// recorded payments, and the admitted open items — never a draft, never a second copy of a fact. DEC-FIN-006: an F-03
// allocation is not a ledger movement (it only applies an existing source to an open item), so it is not a row and
// never moves the balance. OPEN-018 (carry-forward / devir) is open: there is NO opening balance; the running balance
// starts at zero on the first day of the range and says so. OPEN-012: currencies are never converted or added — every
// currency has its own rows, totals and balance.

/// <summary>What recorded Finance fact a ledger row is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartyLedgerEntryKind>))]
public enum PartyLedgerEntryKind
{
    /// <summary>An admitted open item (an approved official invoice installment, DEC-FIN-005): the party's debt.</summary>
    OpenItem = 1,

    /// <summary>A recorded manual bank / POS receipt's money event: the party's credit (POS: the gross, DEC-FIN-014).</summary>
    ManualReceipt = 2,

    /// <summary>The counter-event of an approved FIN-T06 reversal: its own row, the reverse side of the original.</summary>
    ManualReceiptReversal = 3,

    /// <summary>A customer cheque / note collection's money event (DEC-FIN-016): the party's credit at face value.</summary>
    CustomerInstrumentCollection = 4,

    /// <summary>A recorded manual outgoing bank payment's money event (R47): the payment to the party.</summary>
    ManualBankPayment = 5,
}

/// <summary>The column a row is written in, from the company's view of the party's account: borç / alacak.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartyLedgerSide>))]
public enum PartyLedgerSide
{
    Debit = 1,
    Credit = 2,
}

/// <summary>The side of a balance: zero, a debit balance (the party owes) or a credit balance.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartyLedgerBalanceSide>))]
public enum PartyLedgerBalanceSide
{
    Zero = 0,
    Debit = 1,
    Credit = 2,
}

/// <summary>
/// How the range's first balance is found. OPEN-018 is open, so the only value is <see cref="NoneOpen018"/>: nothing
/// before the range is carried in, the running balance starts at zero, and the screen says so.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartyLedgerCarryForward>))]
public enum PartyLedgerCarryForward
{
    NoneOpen018 = 1,
}

/// <summary>One party account of the context company and an inclusive business-date range.</summary>
public sealed record PartyLedgerRequest(Guid PartyId, Guid CompanyAccountId, DateOnly From, DateOnly To);

/// <summary>
/// A balance: its side and its non-negative amount. Without the ledger amount grant both are withheld — the side of a
/// balance is derived from the amounts, so it is <c>null</c> whenever the amount is not <c>Visible</c>.
/// </summary>
public sealed record PartyLedgerBalanceView(PartyLedgerBalanceSide? Side, AmountView Amount);

/// <summary>
/// One recorded fact. <paramref name="EntryId"/> is the fact's own id (the money event or the open item);
/// <paramref name="SourceId"/> is what the row opens: the receipt source (receipt, reversal, collection), the payment,
/// or the open item. <paramref name="ReversesSourceId"/> names the original of a reversal row; <paramref name="InstrumentId"/>
/// the collected cheque / note; <paramref name="DocumentId"/> and <paramref name="DueOn"/> the open item's official
/// document and installment due date. An open item's <paramref name="OccurredOn"/> is the UTC day Finance admitted it
/// (the source contract carries no document date yet — see the module README).
/// </summary>
public sealed record PartyLedgerEntry(
    PartyLedgerEntryKind Kind,
    Guid EntryId,
    DateOnly OccurredOn,
    DateTimeOffset RecordedAtUtc,
    PartyLedgerSide Side,
    AmountView Amount,
    PartyLedgerBalanceView RunningBalance,
    Guid SourceId,
    Guid? ReversesSourceId,
    Guid? InstrumentId,
    Guid? DocumentId,
    DateOnly? DueOn);

/// <summary>One currency's rows in date order, their debit and credit totals, and the range's closing balance.</summary>
public sealed record PartyLedgerCurrency(
    Guid CurrencyId,
    IReadOnlyList<PartyLedgerEntry> Entries,
    AmountView DebitTotal,
    AmountView CreditTotal,
    PartyLedgerBalanceView ClosingBalance);

public sealed record PartyLedgerView(
    Guid CompanyId,
    Guid PartyId,
    Guid CompanyAccountId,
    DateOnly From,
    DateOnly To,
    PartyLedgerCarryForward CarryForward,
    IReadOnlyList<PartyLedgerCurrency> Currencies);

public enum PartyLedgerOutcome
{
    Unavailable,
    Denied,

    /// <summary>A malformed request: an empty id, or a range that ends before it starts.</summary>
    Invalid,

    /// <summary>More rows than one answer carries (<see cref="PartyLedgerRules.MaximumEntries"/>): narrow the range.</summary>
    TooManyEntries,
    Found,
}

public sealed record PartyLedgerResult(PartyLedgerOutcome Outcome, PartyLedgerView? Ledger);

public static class PartyLedgerRules
{
    /// <summary>
    /// ASSUMPTION-FIN-LEDGER-ROWS (technical, reversible): one answer carries at most this many rows; a range with more
    /// is refused with <see cref="TooManyEntriesCode"/> rather than cut, because a cut ledger would show a wrong balance.
    /// Not a business policy — a response-size guard until paging with a carried balance exists.
    /// </summary>
    public const int MaximumEntries = 1000;

    public const string TooManyEntriesCode = "finance_party_ledger_too_many_entries";
}

/// <summary>
/// The party ledger read (<c>ReadPartyLedger</c>; every amount, total and balance only with a fresh
/// <c>ReadPartyLedgerAmount</c>). Company-fenced: only the context company's facts are read.
/// </summary>
public interface IPartyLedgerService
{
    Task<PartyLedgerResult> ReadAuthorizedAsync(FinanceContext context, PartyLedgerRequest request,
        CancellationToken cancellationToken);
}
