using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

public sealed class InvoiceLineCogsAttributedCorrectionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void A_proven_partial_component_preserves_the_aggregate_and_both_actual_account_sides(int sign)
    {
        var world = new AccountingWorld(); var original = world.PostOriginal(amount: 100m); var next = Guid.NewGuid();
        var component = Component(original, sign * 33.34m);
        var pair = InvoiceLineCogsCorrection.CreateAttributed(original, component, next, world.OpenPeriod, world.InPeriod,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(100m, original.Lines[0].Debit); Assert.Equal(100m, original.Lines[1].Credit);
        Assert.All(new[] { pair.Reversing, pair.Replacement }, journal =>
        {
            Assert.Equal(JournalKind.Correction, journal.Kind); Assert.Equal(original.Id, journal.LinkedJournalId);
            Assert.Equal(original.Source, journal.Source); Assert.Equal(original.CurrencyId, journal.CurrencyId);
            Assert.Equal(33.34m, journal.Lines.Sum(x => x.Debit)); Assert.Equal(33.34m, journal.Lines.Sum(x => x.Credit));
        });
        var changed = Assert.Single(pair.Replacement.Lines, x => x.Account.AccountId == next);
        Assert.Equal(sign > 0 ? 33.34m : 0m, changed.Debit);
        Assert.Equal(sign < 0 ? 33.34m : 0m, changed.Credit);
        Assert.Contains(pair.Replacement.Lines, x => x.Account.AccountId == component.CounterAccountId);
    }

    [Fact]
    public void The_ordinary_factory_still_refuses_a_partial_amount_without_attribution()
    {
        var world = new AccountingWorld(); var original = world.PostOriginal(amount: 100);
        Assert.Throws<AccountingDomainException>(() => InvoiceLineCogsCorrection.Create(original, 33.34m,
            world.DebitAccount.AccountId, world.CreditAccount.AccountId, Guid.NewGuid(), world.OpenPeriod, world.InPeriod,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("company")]
    [InlineData("journal")]
    [InlineData("currency")]
    [InlineData("account")]
    [InlineData("source")]
    [InlineData("proof")]
    public void Attribution_cannot_relabel_a_different_journal_currency_company_account_or_amount(string mutation)
    {
        var world = new AccountingWorld(); var original = world.PostOriginal(amount: 100); var proof = Component(original, 33.34m);
        proof = mutation switch
        {
            "amount" => proof with { SignedAmount = 101 }, "company" => proof with { CompanyId = Guid.NewGuid() },
            "journal" => proof with { JournalId = Guid.NewGuid() }, "currency" => proof with { CurrencyId = Guid.NewGuid() },
            "account" => proof with { CogsAccountId = Guid.NewGuid() }, "source" => proof with { JournalSourceId = Guid.NewGuid() },
            _ => proof with { AttributionFingerprint = "unproven" },
        };
        Assert.Throws<AccountingDomainException>(() => InvoiceLineCogsCorrection.CreateAttributed(original, proof, Guid.NewGuid(),
            world.OpenPeriod, world.InPeriod, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }

    private static AttributedPostedJournalComponent Component(Journal original, decimal signed) => new(original.CompanyId,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, original.Id, original.Source.SourceId, original.Source.PostingPurpose,
        original.CurrencyId, original.Lines[0].Debit, signed,
        original.Lines[signed > 0 ? 0 : 1].Account.AccountId, original.Lines[signed > 0 ? 1 : 0].Account.AccountId, new('a', 64));
}
