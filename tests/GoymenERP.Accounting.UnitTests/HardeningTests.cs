using System.Reflection;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// ACC-2: stable rule codes asserted by code, read-only views that a cast cannot reach, the
/// correction's content under one identity, and queue entries that follow the latest delivery.
/// </summary>
public sealed class HardeningTests
{
    private static string CodeOf(Action action) => Assert.Throws<AccountingDomainException>(action).Code;

    [Fact]
    public void Every_rule_code_is_distinct_and_in_the_accounting_namespace()
    {
        var codes = typeof(AccountingErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(codes);
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.StartsWith("accounting_", code, StringComparison.Ordinal));
    }

    [Fact]
    public void Reversing_or_correcting_an_unknown_journal_is_journal_not_found()
    {
        var world = new AccountingWorld();

        Assert.Equal(AccountingErrorCodes.JournalNotFound, CodeOf(() =>
            world.Ledger.Reverse(AccountingWorld.Consumer, "r-1", Guid.NewGuid(), Guid.NewGuid(), world.InPeriod, world.OpenPeriod)));
        Assert.Equal(AccountingErrorCodes.JournalNotFound, CodeOf(() => world.Correct("c-1", Guid.NewGuid(), Guid.NewGuid())));
        Assert.Empty(world.Ledger.Journals);
        // Nothing was recorded, so both deliveries can still carry a real request.
        var original = world.PostOriginal("m-1");
        Assert.Equal(PostingOutcomeKind.Posted, world.Correct("c-1", Guid.NewGuid(), original.Id).Kind);
    }

    [Fact]
    public void A_null_line_is_a_caller_error_not_a_rule()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");

        Assert.Throws<ArgumentException>(() => world.Ledger.Receive(
            world.Request("m-2", purpose: "synthetic-purpose-b") with { Lines = [null!] }, world.OpenPeriod));
        Assert.Throws<ArgumentException>(() => world.Ledger.Correct(
            AccountingWorld.Consumer, "c-1", Guid.NewGuid(), original.Id, Guid.NewGuid(), "synthetic-rule-2",
            world.InPeriod, [new JournalLine(world.DebitAccount, 5m, 0m), null!], world.OpenPeriod));
        Assert.Single(world.Ledger.Journals);
    }

    [Fact]
    public void The_same_correction_id_with_other_lines_is_a_content_conflict_and_records_nothing()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        var correctionId = Guid.NewGuid();
        var first = world.Correct("c-1", correctionId, original.Id, amount: 5m);

        Assert.Equal(AccountingErrorCodes.CorrectionContentConflict,
            CodeOf(() => world.Correct("c-2", correctionId, original.Id, amount: 6m)));

        Assert.Equal(2, world.Ledger.Journals.Count);
        // The refused delivery was not recorded: the same message can still carry the right content.
        var again = world.Correct("c-2", correctionId, original.Id, amount: 5m);
        Assert.Equal(PostingOutcomeKind.AlreadyPosted, again.Kind);
        Assert.Same(first.Journal, again.Journal);
    }

    [Fact]
    public void The_same_correction_id_with_another_business_date_is_a_content_conflict()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        var correctionId = Guid.NewGuid();
        world.Correct("c-1", correctionId, original.Id);

        var otherDay = AccountingWorld.Dates(new DateOnly(2026, 9, 16));
        Assert.Equal(AccountingErrorCodes.CorrectionContentConflict, CodeOf(() => world.Ledger.Correct(
            AccountingWorld.Consumer, "c-2", correctionId, original.Id, Guid.NewGuid(), "synthetic-rule-2", otherDay,
            [new JournalLine(world.DebitAccount, 5m, 0m), new JournalLine(world.CreditAccount, 0m, 5m)], world.OpenPeriod)));
        Assert.Equal(2, world.Ledger.Journals.Count);
    }

    [Fact]
    public void Line_order_recording_instant_rule_version_and_proposed_journal_id_are_not_content()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        var correctionId = Guid.NewGuid();
        var first = world.Correct("c-1", correctionId, original.Id);
        var laterRecording = new PostingDates(world.InPeriod.TransactionDate, world.InPeriod.AccountingDate,
            world.InPeriod.ValueDate, world.InPeriod.RecordedAtUtc.AddHours(2));

        var again = world.Ledger.Correct(
            AccountingWorld.Consumer, "c-2", correctionId, original.Id, Guid.NewGuid(), "synthetic-rule-3", laterRecording,
            [new JournalLine(world.CreditAccount, 0m, 5.00m), new JournalLine(world.DebitAccount, 5m, 0m)], world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.AlreadyPosted, again.Kind);
        Assert.Same(first.Journal, again.Journal);
        Assert.Equal(2, world.Ledger.Journals.Count);
    }

    [Fact]
    public void An_unmapped_and_unvalued_delivery_refreshes_the_pending_entry()
    {
        var world = new AccountingWorld();
        world.Ledger.Receive(world.Request("m-1", amount: null), world.OpenPeriod);
        var before = Assert.Single(world.Ledger.PendingValuations);
        Assert.Equal(2, before.LinesAwaitingAmount);

        var request = world.Request("m-2", ruleVersion: "synthetic-rule-2", mapCredit: false) with
        {
            Lines =
            [
                new ProposedLine("synthetic-debit-key", world.DebitAccount, PostingSide.Debit, 100m),
                new ProposedLine("synthetic-credit-key", null, PostingSide.Credit, null),
            ],
        };
        var outcome = world.Ledger.Receive(request, world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.ExceptionQueued, outcome.Kind);
        var pending = Assert.Single(world.Ledger.PendingValuations);
        Assert.Equal((1, "synthetic-rule-2", "m-1"), (pending.LinesAwaitingAmount, pending.PostingRuleVersion, pending.FirstMessageId));
        Assert.Single(world.Ledger.OpenExceptions);
    }

    [Fact]
    public void A_first_delivery_with_both_gaps_is_visible_in_both_queues()
    {
        var world = new AccountingWorld();

        var outcome = world.Ledger.Receive(world.Request("m-1", amount: null, mapCredit: false), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.ExceptionQueued, outcome.Kind);
        Assert.Equal(["synthetic-credit-key"], Assert.Single(world.Ledger.OpenExceptions).MissingMappingKeys);
        Assert.Equal(2, Assert.Single(world.Ledger.PendingValuations).LinesAwaitingAmount);
        Assert.Empty(world.Ledger.Journals);
    }

    [Fact]
    public void A_failed_posting_leaves_both_queue_entries_as_they_were()
    {
        var world = new AccountingWorld();
        world.Ledger.Receive(world.Request("m-1", amount: null), world.OpenPeriod);
        world.OpenPeriod.Close(AccountingWorld.Closer, AccountingWorld.ClosedAt);

        Assert.Equal(AccountingErrorCodes.PeriodClosed,
            CodeOf(() => world.Ledger.Receive(world.Request("m-2"), world.OpenPeriod)));

        Assert.Single(world.Ledger.PendingValuations);
        Assert.Empty(world.Ledger.Journals);
    }

    [Fact]
    public void The_ledger_views_cannot_be_used_to_change_the_ledger()
    {
        var world = new AccountingWorld();
        var journal = world.PostOriginal("m-1");
        world.Ledger.Receive(world.Request("m-2", purpose: "synthetic-purpose-b", mapCredit: false), world.OpenPeriod);
        world.Ledger.Receive(world.Request("m-3", purpose: "synthetic-purpose-c", amount: null), world.OpenPeriod);

        Assert.Throws<NotSupportedException>(() => ((ICollection<Journal>)world.Ledger.Journals).Add(journal));
        Assert.Throws<NotSupportedException>(() => ((ICollection<Journal>)world.Ledger.Journals).Clear());
        Assert.Throws<NotSupportedException>(() => ((ICollection<PostingException>)world.Ledger.OpenExceptions).Clear());
        Assert.Throws<NotSupportedException>(() => ((ICollection<PendingValuation>)world.Ledger.PendingValuations).Clear());
        Assert.Single(world.Ledger.Journals);
        Assert.Single(world.Ledger.OpenExceptions);
        Assert.Single(world.Ledger.PendingValuations);
    }

    [Fact]
    public void Journal_lines_and_missing_mapping_keys_are_not_arrays_a_cast_can_rewrite()
    {
        var world = new AccountingWorld();
        var journal = world.PostOriginal("m-1");
        var queued = world.Ledger.Receive(world.Request("m-2", purpose: "synthetic-purpose-b", mapCredit: false), world.OpenPeriod).Exception!;

        Assert.False(journal.Lines is JournalLine[]);
        Assert.Throws<NotSupportedException>(() => ((IList<JournalLine>)journal.Lines)[0] = journal.Lines[1]);
        Assert.False(queued.MissingMappingKeys is string[]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)queued.MissingMappingKeys)[0] = "rewritten");
        Assert.Equal(["synthetic-credit-key"], Assert.Single(world.Ledger.OpenExceptions).MissingMappingKeys);
        Assert.Equal(journal.TotalDebit, journal.TotalCredit);
    }
}
