using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinancePartyBalanceTransferPolicyTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Party = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid Currency = Guid.NewGuid();

    [Fact]
    public void Full_balance_keeps_each_currency_side_and_exact_scale_without_fx_or_partial_selection()
    {
        var second = Guid.NewGuid();
        var answer = FinancePartyBalanceTransferPolicy.Currencies(new(Company, Party, Account,
            [Line(Currency, "100.0100"), Line(second, "0.0001") with { Balance = new(PartyLedgerBalanceSide.Credit, Amount(second, "0.0001")) }]));
        Assert.Equal("100.0100", answer.Single(x => x.CurrencyId == Currency).Amount);
        Assert.Equal("0.0001", answer.Single(x => x.CurrencyId == second).Amount);
        Assert.Equal(PartyLedgerBalanceSide.Credit, answer.Single(x => x.CurrencyId == second).Side);
        Assert.Equal(2, answer.Count);
    }

    [Fact]
    public void Hidden_amount_foreign_currency_duplicate_currency_and_inconsistent_zero_are_refused()
    {
        var good = Line(Currency, "100.00");
        var hidden = good with { Balance = new(null, new(AmountDisclosure.Redacted, null)) };
        var foreign = good with { OpenPayable = Amount(Guid.NewGuid(), "0.00") };
        var invalidZero = good with { Balance = new(PartyLedgerBalanceSide.Zero, Amount(Currency, "1.00")) };
        foreach (var broken in new[] { hidden, foreign, invalidZero })
            Assert.Throws<InvalidOperationException>(() => FinancePartyBalanceTransferPolicy.Currencies(new(Company, Party, Account, [broken])));
        Assert.Throws<InvalidOperationException>(() => FinancePartyBalanceTransferPolicy.Currencies(new(Company, Party, Account, [good, good])));
    }

    [Fact]
    public void Snapshot_fences_owner_version_balance_and_existing_allocation_beneficiary_without_amount_rounding()
    {
        var owner = new FinancePartyBalanceTransferOwnerProofV1(Company, Party, Account, "3", "4", "SENTETIK-P", "SENTETIK eski",
            Guid.NewGuid(), Guid.NewGuid(), "5", "6", "SENTETIK-S", "SENTETIK yeni", FinancePartyBalanceTransferLinkKindV1.Succession,
            new(2026, 11, 17), "SENTETIK dayanak", DateTimeOffset.UnixEpoch);
        var currencies = FinancePartyBalanceTransferPolicy.Currencies(new(Company, Party, Account, [Line(Currency, "100.00")]));
        var source = new FinanceBalanceTransferSourceFact(1, Guid.NewGuid(), Party, Account, Currency, 2, null, "100.00", "100.00", Party, Account, 0);
        var version = FinancePartyBalanceTransferPolicy.SnapshotVersion(owner, currencies, [source]);
        Assert.Matches("^[A-F0-9]{64}$", version);
        Assert.NotEqual(version, FinancePartyBalanceTransferPolicy.SnapshotVersion(owner with { SuccessorAccountVersion = "7" }, currencies, [source]));
        Assert.NotEqual(version, FinancePartyBalanceTransferPolicy.SnapshotVersion(owner, currencies, [source with { SourceVersion = 3 }]));
        Assert.NotEqual(version, FinancePartyBalanceTransferPolicy.SnapshotVersion(owner, currencies, [source with { AvailableAmount = "99.99" }]));
        Assert.Throws<InvalidOperationException>(() => FinancePartyBalanceTransferPolicy.SnapshotVersion(owner, currencies,
            [source with { CurrentAccountId = Guid.NewGuid() }]));
    }

    private static AmountView Amount(Guid currency, string amount) => new(AmountDisclosure.Visible, new(amount, currency));
    private static PartyBalanceCurrency Line(Guid currency, string amount) => new(currency, Amount(currency, amount), Amount(currency, "0.00"),
        new(PartyLedgerBalanceSide.Debit, Amount(currency, amount)), Amount(currency, amount), Amount(currency, "0.00"));
}
