using System.Reflection;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// Delivery vs business identity (R4), exception queue and pending valuation (§7.6, R6), and
/// reversal/correction as new linked journals.
/// </summary>
public sealed class SourcePostingTests
{
    [Fact]
    public void A_redelivered_message_has_no_new_effect()
    {
        var world = new AccountingWorld();
        var first = world.PostOriginal("m-1");

        var again = world.Ledger.Receive(world.Request("m-1"), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.DuplicateMessage, again.Kind);
        Assert.Null(again.Journal);
        Assert.Equal([first], world.Ledger.Journals);
    }

    [Fact]
    public void The_same_source_under_a_different_message_id_does_not_make_a_second_original()
    {
        // Plan §13: "Aynı fiş kaynağı farklı messageId ile gelir → iş kaynağı tekilliği ikinci asıl fişi engeller."
        var world = new AccountingWorld();
        var first = world.PostOriginal("m-1");

        var second = world.Ledger.Receive(world.Request("m-2"), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.AlreadyPosted, second.Kind);
        Assert.Same(first, second.Journal);
        Assert.Single(world.Ledger.Journals);
    }

    [Fact]
    public void A_new_posting_rule_version_does_not_make_a_second_original()
    {
        var world = new AccountingWorld();
        var first = world.PostOriginal("m-1");

        var newer = world.Ledger.Receive(world.Request("m-2", ruleVersion: "synthetic-rule-2"), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.AlreadyPosted, newer.Kind);
        Assert.Same(first, newer.Journal);
        Assert.Equal("synthetic-rule-1", world.Ledger.FindOriginal(world.SourceId, AccountingWorld.Purpose)!.PostingRuleVersion);
        Assert.Single(world.Ledger.Journals);
    }

    [Fact]
    public void A_different_posting_purpose_of_the_same_source_is_its_own_original()
    {
        var world = new AccountingWorld();
        world.PostOriginal("m-1");

        var other = world.Ledger.Receive(world.Request("m-2", purpose: "synthetic-purpose-b"), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.Posted, other.Kind);
        Assert.Equal(2, world.Ledger.Journals.Count);
    }

    [Fact]
    public void A_missing_account_mapping_goes_to_the_exception_queue_not_to_a_guessed_account()
    {
        var world = new AccountingWorld();

        var outcome = world.Ledger.Receive(world.Request("m-1", mapCredit: false), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.ExceptionQueued, outcome.Kind);
        Assert.Null(outcome.Journal);
        Assert.Empty(world.Ledger.Journals);
        var queued = Assert.Single(world.Ledger.OpenExceptions);
        Assert.Equal(["synthetic-credit-key"], queued.MissingMappingKeys);
        Assert.Equal(world.SourceId, queued.Source.SourceId);
    }

    [Fact]
    public void Redelivering_an_unmapped_source_does_not_duplicate_the_queue_and_a_mapped_delivery_posts_once()
    {
        var world = new AccountingWorld();
        world.Ledger.Receive(world.Request("m-1", mapCredit: false), world.OpenPeriod);
        world.Ledger.Receive(world.Request("m-2", mapCredit: false), world.OpenPeriod);
        Assert.Single(world.Ledger.OpenExceptions);

        var mapped = world.Ledger.Receive(world.Request("m-3"), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.Posted, mapped.Kind);
        Assert.Empty(world.Ledger.OpenExceptions);
        Assert.Single(world.Ledger.Journals);
    }

    [Fact]
    public void An_unknown_amount_is_a_visible_pending_valuation_not_a_zero_journal()
    {
        var world = new AccountingWorld();

        var outcome = world.Ledger.Receive(world.Request("m-1", amount: null), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.WaitingValuation, outcome.Kind);
        Assert.Empty(world.Ledger.Journals);
        var pending = Assert.Single(world.Ledger.PendingValuations);
        Assert.Equal(2, pending.LinesAwaitingAmount);
        Assert.Equal(world.SourceId, pending.Source.SourceId);
    }

    [Fact]
    public void A_valued_delivery_clears_the_pending_entry_and_posts_one_original()
    {
        var world = new AccountingWorld();
        world.Ledger.Receive(world.Request("m-1", amount: null), world.OpenPeriod);

        var valued = world.Ledger.Receive(world.Request("m-2", amount: 250m), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.Posted, valued.Kind);
        Assert.Empty(world.Ledger.PendingValuations);
        Assert.Equal(250m, Assert.Single(world.Ledger.Journals).TotalDebit);
    }

    [Fact]
    public void A_reversal_is_a_new_journal_that_mirrors_and_links_to_the_original()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");

        var outcome = world.Ledger.Reverse(
            AccountingWorld.Consumer, "r-1", original.Id, Guid.NewGuid(), world.InPeriod, world.OpenPeriod);

        var reversal = outcome.Journal!;
        Assert.Equal(PostingOutcomeKind.Posted, outcome.Kind);
        Assert.Equal(JournalKind.Reversal, reversal.Kind);
        Assert.NotEqual(original.Id, reversal.Id);
        Assert.Equal(original.Id, reversal.LinkedJournalId);
        Assert.Equal(original.Source, reversal.Source);
        Assert.Equal(original.Lines.Select(l => (l.Account, l.Credit, l.Debit)), reversal.Lines.Select(l => (l.Account, l.Debit, l.Credit)));
        // The original is untouched and still the source's one original.
        Assert.Same(original, world.Ledger.FindOriginal(world.SourceId, AccountingWorld.Purpose));
        Assert.Equal(2, world.Ledger.Journals.Count);
    }

    [Fact]
    public void A_journal_is_reversed_at_most_once_and_a_replay_reverses_nothing()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        var first = world.Ledger.Reverse(AccountingWorld.Consumer, "r-1", original.Id, Guid.NewGuid(), world.InPeriod, world.OpenPeriod);

        var replay = world.Ledger.Reverse(AccountingWorld.Consumer, "r-1", original.Id, Guid.NewGuid(), world.InPeriod, world.OpenPeriod);
        var again = world.Ledger.Reverse(AccountingWorld.Consumer, "r-2", original.Id, Guid.NewGuid(), world.InPeriod, world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.DuplicateMessage, replay.Kind);
        Assert.Equal(PostingOutcomeKind.AlreadyReversed, again.Kind);
        Assert.Same(first.Journal, again.Journal);
        Assert.Equal(2, world.Ledger.Journals.Count);
    }

    [Fact]
    public void A_reversal_itself_is_not_reversed()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        var reversal = world.Ledger.Reverse(AccountingWorld.Consumer, "r-1", original.Id, Guid.NewGuid(), world.InPeriod, world.OpenPeriod).Journal!;

        var error = Assert.Throws<AccountingDomainException>(
            () => world.Ledger.Reverse(AccountingWorld.Consumer, "r-2", reversal.Id, Guid.NewGuid(), world.InPeriod, world.OpenPeriod));

        Assert.Equal(AccountingErrorCodes.ReversalOfReversal, error.Code);
    }

    [Fact]
    public void A_correction_of_a_closed_period_journal_lands_as_a_new_linked_journal_in_an_open_period()
    {
        var world = new AccountingWorld();
        var august = new AccountingPeriod(Guid.NewGuid(), world.Company, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        var original = world.Ledger.Receive(
            world.Request("m-1") with { Dates = AccountingWorld.Dates(new DateOnly(2026, 8, 20)) }, august).Journal!;
        august.Close();

        var outcome = world.Ledger.Correct(
            AccountingWorld.Consumer,
            "c-1",
            original.Id,
            Guid.NewGuid(),
            "synthetic-rule-2",
            world.InPeriod,
            [new JournalLine(world.DebitAccount, 5m, 0m), new JournalLine(world.CreditAccount, 0m, 5m)],
            world.OpenPeriod);

        var correction = outcome.Journal!;
        Assert.Equal(JournalKind.Correction, correction.Kind);
        Assert.Equal(original.Id, correction.LinkedJournalId);
        Assert.Equal(new DateOnly(2026, 8, 20), original.Dates.AccountingDate);
        Assert.Same(original, world.Ledger.FindOriginal(world.SourceId, AccountingWorld.Purpose));
    }

    [Fact]
    public void A_correction_cannot_be_dated_into_a_closed_period()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        world.OpenPeriod.Close();

        var error = Assert.Throws<AccountingDomainException>(() => world.Ledger.Correct(
            AccountingWorld.Consumer,
            "c-1",
            original.Id,
            Guid.NewGuid(),
            "synthetic-rule-2",
            world.InPeriod,
            [new JournalLine(world.DebitAccount, 5m, 0m), new JournalLine(world.CreditAccount, 0m, 5m)],
            world.OpenPeriod));

        Assert.Equal(AccountingErrorCodes.PeriodClosed, error.Code);
        Assert.Single(world.Ledger.Journals);
    }

    [Fact]
    public void Nothing_can_delete_or_edit_a_posted_journal()
    {
        var mutators = new[] { typeof(Journal), typeof(SourcePostingLedger), typeof(JournalLine) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.Name.StartsWith("Delete", StringComparison.Ordinal)
                || method.Name.StartsWith("Remove", StringComparison.Ordinal)
                || method.Name.StartsWith("Update", StringComparison.Ordinal)
                || method.Name.StartsWith("Edit", StringComparison.Ordinal))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}");
        var writableJournalProperties = typeof(Journal)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name);

        Assert.Empty(mutators);
        Assert.Empty(writableJournalProperties);
        Assert.False(typeof(IList<JournalLine>).IsAssignableFrom(typeof(Journal).GetProperty(nameof(Journal.Lines))!.PropertyType));
    }
}
