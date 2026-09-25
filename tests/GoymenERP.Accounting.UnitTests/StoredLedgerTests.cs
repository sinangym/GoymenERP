using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// ACC-3 domain restore: a ledger rebuilt from stored facts answers exactly as the ledger that
/// produced them, and a stored fact that breaks a rule is refused rather than trusted.
/// </summary>
public sealed class StoredLedgerTests
{
    private static string CodeOf(Action action) => Assert.Throws<AccountingDomainException>(action).Code;

    [Fact]
    public void A_restored_original_still_makes_a_new_delivery_already_posted()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        var restored = Restore(original);

        var ledger = SourcePostingLedger.FromStored(world.Company, [restored], [], [], []);
        var again = ledger.Receive(world.Request("m-2"), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.AlreadyPosted, again.Kind);
        Assert.Same(restored, again.Journal);
        Assert.Single(ledger.Journals);
    }

    [Fact]
    public void A_restored_reversal_still_blocks_a_second_reversal()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        var reversal = world.Ledger.Reverse(AccountingWorld.Consumer, "r-1", original.Id, Guid.NewGuid(), world.InPeriod,
            world.OpenPeriod).Journal!;

        var ledger = SourcePostingLedger.FromStored(world.Company, [Restore(original), Restore(reversal)], [], [], []);
        var again = ledger.Reverse(AccountingWorld.Consumer, "r-2", original.Id, Guid.NewGuid(), world.InPeriod, world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.AlreadyReversed, again.Kind);
        Assert.Equal(reversal.Id, again.Journal!.Id);
    }

    [Fact]
    public void A_restored_correction_identity_still_answers_already_posted()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        var correctionId = Guid.NewGuid();
        var correction = world.Correct("c-1", correctionId, original.Id).Journal!;

        var ledger = SourcePostingLedger.FromStored(world.Company, [Restore(original), Restore(correction)],
            [new StoredCorrection(correctionId, correction.Id)], [], []);
        var again = ledger.Correct(AccountingWorld.Consumer, "c-2", correctionId, original.Id, Guid.NewGuid(), "synthetic-rule-2",
            world.InPeriod, [new JournalLine(world.DebitAccount, 5m, 0m), new JournalLine(world.CreditAccount, 0m, 5m)],
            world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.AlreadyPosted, again.Kind);
        Assert.Equal(correction.Id, again.Journal!.Id);
    }

    [Fact]
    public void Restored_queue_entries_are_updated_by_the_next_delivery()
    {
        var world = new AccountingWorld();
        var queued = world.Ledger.Receive(world.Request("m-1", mapCredit: false), world.OpenPeriod).Exception!;

        var ledger = SourcePostingLedger.FromStored(world.Company, [], [], [queued], []);
        var posted = ledger.Receive(world.Request("m-2"), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.Posted, posted.Kind);
        Assert.Null(ledger.FindOpenException(world.SourceId, AccountingWorld.Purpose));
        Assert.Null(ledger.FindPendingValuation(world.SourceId, AccountingWorld.Purpose));
    }

    [Fact]
    public void Stored_facts_of_another_company_or_duplicated_identities_are_refused()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        var restored = Restore(original);
        var other = new AccountingWorld();
        var foreign = Restore(other.PostOriginal("m-1"));

        Assert.Equal(AccountingErrorCodes.CompanyMismatch,
            CodeOf(() => SourcePostingLedger.FromStored(world.Company, [foreign], [], [], [])));
        Assert.Equal(AccountingErrorCodes.InvalidReference,
            CodeOf(() => SourcePostingLedger.FromStored(world.Company, [restored, restored], [], [], [])));
        Assert.Equal(AccountingErrorCodes.InvalidReference,
            CodeOf(() => SourcePostingLedger.FromStored(world.Company, [restored], [new StoredCorrection(Guid.NewGuid(), restored.Id)], [], [])));
    }

    [Fact]
    public void A_stored_journal_is_checked_again_but_not_against_its_period()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        world.OpenPeriod.Close(AccountingWorld.Closer, AccountingWorld.ClosedAt);

        // Its period has closed since: the stored journal is still what was posted.
        var restored = Restore(original);
        Assert.Equal(original.TotalDebit, restored.TotalCredit);

        Assert.Equal(AccountingErrorCodes.JournalUnbalanced, CodeOf(() => Journal.Restore(Guid.NewGuid(), JournalKind.Original,
            original.Source, "synthetic-rule-1", world.Currency, world.InPeriod,
            [new JournalLine(world.DebitAccount, 5m, 0m), new JournalLine(world.CreditAccount, 0m, 4m)], null)));
        Assert.Equal(AccountingErrorCodes.InvalidReference, CodeOf(() => Journal.Restore(Guid.NewGuid(), JournalKind.Reversal,
            original.Source, "synthetic-rule-1", world.Currency, world.InPeriod, original.Lines, linkedJournalId: null)));
        Assert.Equal(AccountingErrorCodes.InvalidReference, CodeOf(() => Journal.Restore(Guid.NewGuid(), JournalKind.Original,
            original.Source, "synthetic-rule-1", world.Currency, world.InPeriod, original.Lines, linkedJournalId: original.Id)));
    }

    [Fact]
    public void A_stored_period_is_restored_proposed_open_or_closed_with_its_full_trace()
    {
        var company = Guid.NewGuid();
        var september = (new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var proposed = AccountingPeriod.Restore(Guid.NewGuid(), company, september.Item1, september.Item2,
            AccountingWorld.Preparer, AccountingWorld.PreparedAt, null, null, null, null);
        var open = AccountingPeriod.Restore(Guid.NewGuid(), company, september.Item1, september.Item2,
            AccountingWorld.Preparer, AccountingWorld.PreparedAt, AccountingWorld.Approver, AccountingWorld.ApprovedAt, null, null);
        var closed = AccountingPeriod.Restore(Guid.NewGuid(), company, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            AccountingWorld.Preparer, AccountingWorld.PreparedAt, AccountingWorld.Approver, AccountingWorld.ApprovedAt,
            AccountingWorld.Closer, AccountingWorld.ClosedAt);

        Assert.Equal((AccountingPeriodStatus.Proposed, (Guid?)null), (proposed.Status, proposed.ApprovedByUserId));
        Assert.Equal((AccountingPeriodStatus.Open, (Guid?)AccountingWorld.Approver), (open.Status, open.ApprovedByUserId));
        Assert.Equal((AccountingPeriodStatus.Closed, (Guid?)AccountingWorld.Closer, (DateTimeOffset?)AccountingWorld.ClosedAt),
            (closed.Status, closed.ClosedByUserId, closed.ClosedAtUtc));

        // A half trace, a close without an approval, or a stored self-approval is refused, never repaired.
        Assert.Equal(AccountingErrorCodes.InvalidReference, CodeOf(() => AccountingPeriod.Restore(Guid.NewGuid(), company,
            september.Item1, september.Item2, AccountingWorld.Preparer, AccountingWorld.PreparedAt, AccountingWorld.Approver, null, null, null)));
        Assert.Equal(AccountingErrorCodes.InvalidReference, CodeOf(() => AccountingPeriod.Restore(Guid.NewGuid(), company,
            september.Item1, september.Item2, AccountingWorld.Preparer, AccountingWorld.PreparedAt, null, null,
            AccountingWorld.Closer, AccountingWorld.ClosedAt)));
        Assert.Equal(AccountingErrorCodes.InvalidReference, CodeOf(() => AccountingPeriod.Restore(Guid.NewGuid(), company,
            september.Item1, september.Item2, AccountingWorld.Preparer, AccountingWorld.PreparedAt, AccountingWorld.Approver,
            AccountingWorld.ApprovedAt, AccountingWorld.Closer, closedAtUtc: null)));
        Assert.Equal(AccountingErrorCodes.PeriodSelfApproval, CodeOf(() => AccountingPeriod.Restore(Guid.NewGuid(), company,
            september.Item1, september.Item2, AccountingWorld.Preparer, AccountingWorld.PreparedAt, AccountingWorld.Preparer,
            AccountingWorld.ApprovedAt, null, null)));
    }

    private static Journal Restore(Journal journal) =>
        Journal.Restore(journal.Id, journal.Kind, journal.Source, journal.PostingRuleVersion, journal.CurrencyId, journal.Dates,
            journal.Lines, journal.LinkedJournalId);
}
