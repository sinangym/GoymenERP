using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// MOD-15 (FNS-D7): in the party ledger a receivable open item (a sales invoice installment) is the party's debt —
/// debit — and a payable open item (a purchase invoice installment) is the company's debt to the party — credit. The
/// same party's two directions stay two rows; nothing is netted away (ALS-D42), the balance simply shows both.
/// </summary>
public sealed class PartyLedgerPolicyDirectionTests
{
    private static readonly Guid Currency = Guid.Parse("88888888-8888-4888-8888-888888888888");

    [Fact]
    public void A_receivable_is_debit_and_a_payable_is_credit()
    {
        Assert.Equal(PartyLedgerSide.Debit, PartyLedgerPolicy.SideOf(PartyLedgerEntryKind.OpenItem));
        Assert.Equal(PartyLedgerSide.Credit, PartyLedgerPolicy.SideOf(PartyLedgerEntryKind.PayableOpenItem));
    }

    [Fact]
    public void A_payment_is_debit_and_its_approved_reversal_is_credit()
    {
        // API-23: the reversal is the reverse side of the payment it reverses, as its own row.
        Assert.Equal(PartyLedgerSide.Debit, PartyLedgerPolicy.SideOf(PartyLedgerEntryKind.ManualBankPayment));
        Assert.Equal(PartyLedgerSide.Credit, PartyLedgerPolicy.SideOf(PartyLedgerEntryKind.ManualBankPaymentReversal));
    }

    [Fact]
    public void A_party_that_is_customer_and_supplier_keeps_both_rows_and_a_balance_of_the_difference()
    {
        var at = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        var facts = new[]
        {
            Fact(PartyLedgerEntryKind.OpenItem, new DateOnly(2026, 11, 1), at, "1000.00"),
            Fact(PartyLedgerEntryKind.PayableOpenItem, new DateOnly(2026, 11, 2), at, "400.00"),
        };

        var currency = Assert.Single(PartyLedgerPolicy.Build(facts, AmountDisclosure.Visible));

        Assert.Equal([PartyLedgerSide.Debit, PartyLedgerSide.Credit], currency.Entries.Select(entry => entry.Side));
        Assert.Equal("1000.00", currency.DebitTotal.Money?.Amount);
        Assert.Equal("400.00", currency.CreditTotal.Money?.Amount);
        Assert.Equal(PartyLedgerBalanceSide.Debit, currency.ClosingBalance.Side);
        Assert.Equal("600.00", currency.ClosingBalance.Amount.Money?.Amount);
    }

    private static PartyLedgerFact Fact(PartyLedgerEntryKind kind, DateOnly on, DateTimeOffset at, string amount) =>
        new(kind, Guid.NewGuid(), on, at, Currency, amount, Guid.NewGuid(), null, null, Guid.NewGuid(), null);
}
