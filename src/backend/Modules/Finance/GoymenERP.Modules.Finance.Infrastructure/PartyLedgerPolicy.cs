using System.Numerics;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// One recorded Finance fact of a party account as the ledger reads it; a projection — no amount leaves unless
/// disclosed. The amount is the stored exact text of the fact (a POS receipt's gross, DEC-FIN-014).
/// </summary>
internal sealed record PartyLedgerFact(
    PartyLedgerEntryKind Kind,
    Guid EntryId,
    DateOnly OccurredOn,
    DateTimeOffset RecordedAtUtc,
    Guid CurrencyId,
    string AmountText,
    Guid SourceId,
    Guid? ReversesSourceId,
    Guid? InstrumentId,
    Guid? DocumentId,
    DateOnly? DueOn);

/// <summary>
/// The first-release party ledger, pure: each currency's facts in date order (then the recording instant, the kind and
/// the id, so the order never depends on the database), a running balance that starts at ZERO on the range's first
/// day (OPEN-018: no carry-forward), and the debit / credit totals and closing balance. Exact decimal text only — no
/// floating point, no rounding, no local digit limit; currencies are never converted or added (OPEN-012). Every
/// amount of a currency, a zero included, is written at the widest scale that currency's facts carry.
/// <para>
/// The side of a row follows its kind, from the company's view of the party's account: an open item (the party's
/// invoice installment) and a payment to the party are debit (borç); a receipt and a cheque / note collection are
/// credit (alacak); a reversal is the reverse side of the receipt it reverses (debit), as its own row — the original
/// row stays as it was recorded.
/// </para>
/// </summary>
internal static class PartyLedgerPolicy
{
    public static PartyLedgerSide SideOf(PartyLedgerEntryKind kind) => kind switch
    {
        PartyLedgerEntryKind.OpenItem => PartyLedgerSide.Debit,
        PartyLedgerEntryKind.ManualReceipt => PartyLedgerSide.Credit,
        PartyLedgerEntryKind.ManualReceiptReversal => PartyLedgerSide.Debit,
        PartyLedgerEntryKind.CustomerInstrumentCollection => PartyLedgerSide.Credit,
        PartyLedgerEntryKind.ManualBankPayment => PartyLedgerSide.Debit,
        _ => throw new InvalidOperationException("Cari ekstre satır türü tanınmadı; ekstre hesaplanmaz."),
    };

    public static IReadOnlyList<PartyLedgerCurrency> Build(IEnumerable<PartyLedgerFact> facts, AmountDisclosure disclosure)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return
        [
            .. facts
                .GroupBy(fact => fact.CurrencyId)
                .OrderBy(group => group.Key)
                .Select(group => Currency(group.Key, group, disclosure)),
        ];
    }

    private static PartyLedgerCurrency Currency(Guid currencyId, IEnumerable<PartyLedgerFact> facts,
        AmountDisclosure disclosure)
    {
        var ordered = facts
            .OrderBy(fact => fact.OccurredOn)
            .ThenBy(fact => fact.RecordedAtUtc)
            .ThenBy(fact => fact.Kind)
            .ThenBy(fact => fact.EntryId)
            .Select(fact => (Fact: fact, Amount: Parse(fact.AmountText)))
            .ToList();
        var scale = ordered.Count == 0 ? 0 : ordered.Max(item => item.Amount.Scale);

        var entries = new List<PartyLedgerEntry>(ordered.Count);
        var balance = BigInteger.Zero;
        var debit = BigInteger.Zero;
        var credit = BigInteger.Zero;
        foreach (var (fact, amount) in ordered)
        {
            var side = SideOf(fact.Kind);
            var value = amount.Unscaled * BigInteger.Pow(10, scale - amount.Scale);
            if (side == PartyLedgerSide.Debit)
            {
                debit += value;
                balance += value;
            }
            else
            {
                credit += value;
                balance -= value;
            }

            entries.Add(new PartyLedgerEntry(fact.Kind, fact.EntryId, fact.OccurredOn, fact.RecordedAtUtc, side,
                Amount(value, scale, currencyId, disclosure), Balance(balance, scale, currencyId, disclosure),
                fact.SourceId, fact.ReversesSourceId, fact.InstrumentId, fact.DocumentId, fact.DueOn));
        }

        return new PartyLedgerCurrency(currencyId, entries, Amount(debit, scale, currencyId, disclosure),
            Amount(credit, scale, currencyId, disclosure), Balance(balance, scale, currencyId, disclosure));
    }

    private static FinanceExactDecimalText Parse(string amountText) =>
        // A stored fact passed its profile's exact scale; anything else is a broken invariant, never a 0.
        FinanceExactDecimalText.TryParsePositive(amountText, out var amount)
            ? amount
            : throw new InvalidOperationException("Cari ekstre tutarı okunamadı; ekstre hesaplanmaz.");

    private static AmountView Amount(BigInteger value, int scale, Guid currencyId, AmountDisclosure disclosure) =>
        disclosure == AmountDisclosure.Visible
            ? new AmountView(AmountDisclosure.Visible,
                new MoneyText(new FinanceExactDecimalText(BigInteger.Abs(value), scale).Text, currencyId))
            : new AmountView(disclosure, null);

    /// <summary>The balance's side is derived from the amounts, so it is withheld together with them.</summary>
    private static PartyLedgerBalanceView Balance(BigInteger value, int scale, Guid currencyId, AmountDisclosure disclosure) =>
        disclosure == AmountDisclosure.Visible
            ? new PartyLedgerBalanceView(
                value.Sign switch
                {
                    > 0 => PartyLedgerBalanceSide.Debit,
                    < 0 => PartyLedgerBalanceSide.Credit,
                    _ => PartyLedgerBalanceSide.Zero,
                },
                Amount(value, scale, currencyId, disclosure))
            : new PartyLedgerBalanceView(null, new AmountView(disclosure, null));
}
