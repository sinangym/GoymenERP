using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>Balance, line shape, company and period rules of a posted journal (plan §7.6, Ö5).</summary>
public sealed class JournalAndPeriodTests
{
    [Fact]
    public void A_balanced_source_posting_becomes_one_original_journal()
    {
        var world = new AccountingWorld();

        var journal = world.PostOriginal();

        Assert.Equal(JournalKind.Original, journal.Kind);
        Assert.Equal(world.Company, journal.CompanyId);
        Assert.Equal(100m, journal.TotalDebit);
        Assert.Equal(journal.TotalDebit, journal.TotalCredit);
        Assert.Null(journal.LinkedJournalId);
        Assert.Single(world.Ledger.Journals);
    }

    [Fact]
    public void Debit_and_credit_must_match_exactly_with_no_rounding()
    {
        var world = new AccountingWorld();
        var request = world.Request("m-1") with
        {
            Lines =
            [
                new ProposedLine("d", world.DebitAccount, PostingSide.Debit, 100.00m),
                new ProposedLine("c", world.CreditAccount, PostingSide.Credit, 99.999m),
            ],
        };

        var error = Assert.Throws<AccountingDomainException>(() => world.Ledger.Receive(request, world.OpenPeriod));

        Assert.Equal(AccountingErrorCodes.JournalUnbalanced, error.Code);
        Assert.Empty(world.Ledger.Journals);
    }

    [Fact]
    public void Several_lines_balance_on_their_totals()
    {
        var world = new AccountingWorld();
        var third = new AccountRef(world.Company, Guid.NewGuid());
        var request = world.Request("m-1") with
        {
            Lines =
            [
                new ProposedLine("d", world.DebitAccount, PostingSide.Debit, 70m),
                new ProposedLine("d2", third, PostingSide.Debit, 30m),
                new ProposedLine("c", world.CreditAccount, PostingSide.Credit, 100m),
            ],
        };

        var outcome = world.Ledger.Receive(request, world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.Posted, outcome.Kind);
        Assert.Equal(3, outcome.Journal!.Lines.Count);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(-10, 0)]
    [InlineData(0, -10)]
    public void A_line_is_one_positive_side_only(int debit, int credit)
    {
        var world = new AccountingWorld();

        var error = Assert.Throws<AccountingDomainException>(() => new JournalLine(world.DebitAccount, debit, credit));

        Assert.Equal(AccountingErrorCodes.JournalLineInvalid, error.Code);
    }

    [Fact]
    public void A_zero_amount_from_a_rule_is_not_a_posting()
    {
        var world = new AccountingWorld();

        var error = Assert.Throws<AccountingDomainException>(
            () => world.Ledger.Receive(world.Request("m-1", amount: 0m), world.OpenPeriod));

        Assert.Equal(AccountingErrorCodes.JournalLineInvalid, error.Code);
        Assert.Empty(world.Ledger.Journals);
    }

    [Fact]
    public void A_single_line_is_not_a_journal()
    {
        var world = new AccountingWorld();
        var request = world.Request("m-1") with
        {
            Lines = [new ProposedLine("d", world.DebitAccount, PostingSide.Debit, 100m)],
        };

        var error = Assert.Throws<AccountingDomainException>(() => world.Ledger.Receive(request, world.OpenPeriod));

        Assert.Equal(AccountingErrorCodes.JournalTooFewLines, error.Code);
    }

    [Fact]
    public void An_account_of_another_company_is_refused()
    {
        var world = new AccountingWorld();
        var foreign = new AccountRef(world.OtherCompany, Guid.NewGuid());
        var request = world.Request("m-1") with
        {
            Lines =
            [
                new ProposedLine("d", world.DebitAccount, PostingSide.Debit, 100m),
                new ProposedLine("c", foreign, PostingSide.Credit, 100m),
            ],
        };

        var error = Assert.Throws<AccountingDomainException>(() => world.Ledger.Receive(request, world.OpenPeriod));

        Assert.Equal(AccountingErrorCodes.CompanyMismatch, error.Code);
        Assert.Empty(world.Ledger.Journals);
    }

    [Fact]
    public void A_source_of_another_company_is_refused_by_the_ledger()
    {
        var world = new AccountingWorld();
        var request = world.Request("m-1") with { Source = new SourceReference(world.OtherCompany, world.SourceId, AccountingWorld.Purpose) };

        var error = Assert.Throws<AccountingDomainException>(() => world.Ledger.Receive(request, world.OpenPeriod));

        Assert.Equal(AccountingErrorCodes.CompanyMismatch, error.Code);
    }

    [Fact]
    public void A_closed_period_takes_no_posting_and_records_no_delivery()
    {
        var world = new AccountingWorld();
        world.OpenPeriod.Close();

        var error = Assert.Throws<AccountingDomainException>(
            () => world.Ledger.Receive(world.Request("m-1"), world.OpenPeriod));

        Assert.Equal(AccountingErrorCodes.PeriodClosed, error.Code);
        Assert.Empty(world.Ledger.Journals);
        // Nothing was committed, so the same delivery is still new once a valid period applies.
        var open = new AccountingPeriod(Guid.NewGuid(), world.Company, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Equal(PostingOutcomeKind.Posted, world.Ledger.Receive(world.Request("m-1"), open).Kind);
    }

    [Fact]
    public void The_period_must_cover_the_accounting_date_and_belong_to_the_company()
    {
        var world = new AccountingWorld();
        var october = new AccountingPeriod(Guid.NewGuid(), world.Company, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var foreign = new AccountingPeriod(Guid.NewGuid(), world.OtherCompany, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.Equal(
            AccountingErrorCodes.PeriodDoesNotCover,
            Assert.Throws<AccountingDomainException>(() => world.Ledger.Receive(world.Request("m-1"), october)).Code);
        Assert.Equal(
            AccountingErrorCodes.CompanyMismatch,
            Assert.Throws<AccountingDomainException>(() => world.Ledger.Receive(world.Request("m-2"), foreign)).Code);
    }

    [Fact]
    public void The_dates_are_kept_apart_and_none_is_derived()
    {
        var world = new AccountingWorld();
        var dates = new PostingDates(
            TransactionDate: new DateOnly(2026, 8, 28),
            AccountingDate: new DateOnly(2026, 9, 2),
            ValueDate: new DateOnly(2026, 9, 5),
            RecordedAtUtc: new DateTimeOffset(2026, 9, 3, 8, 30, 0, TimeSpan.Zero));

        var outcome = world.Ledger.Receive(world.Request("m-1") with { Dates = dates }, world.OpenPeriod);

        Assert.Equal(dates, outcome.Journal!.Dates);
    }

    [Fact]
    public void The_period_decides_on_the_accounting_date_not_the_transaction_date()
    {
        var world = new AccountingWorld();
        // The document is dated in August, but the posting is accounted for in the open September.
        var dates = new PostingDates(new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 1), null, DateTimeOffset.UnixEpoch);

        var outcome = world.Ledger.Receive(world.Request("m-1") with { Dates = dates }, world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.Posted, outcome.Kind);
    }

    [Fact]
    public void A_period_cannot_end_before_it_starts()
    {
        Assert.Equal(
            AccountingErrorCodes.PeriodInvalid,
            Assert.Throws<AccountingDomainException>(
                () => new AccountingPeriod(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1))).Code);
    }
}
