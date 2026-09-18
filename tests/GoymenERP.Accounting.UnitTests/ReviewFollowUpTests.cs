using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// Correction business identity, replay answers, stale queue entries, canonical identity texts,
/// UTC dates and the period close trace (#283 review follow-up).
/// </summary>
public sealed class ReviewFollowUpTests
{
    [Fact]
    public void The_same_correction_under_a_new_message_id_is_already_posted_and_returns_its_journal()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        var correctionId = Guid.NewGuid();
        var first = world.Correct("c-1", correctionId, original.Id);

        var again = world.Correct("c-2", correctionId, original.Id);

        Assert.Equal(PostingOutcomeKind.Posted, first.Kind);
        Assert.Equal(PostingOutcomeKind.AlreadyPosted, again.Kind);
        Assert.Same(first.Journal, again.Journal);
        Assert.Equal(2, world.Ledger.Journals.Count);
    }

    [Fact]
    public void A_second_different_correction_of_the_same_journal_posts()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        var first = world.Correct("c-1", Guid.NewGuid(), original.Id);

        var second = world.Correct("c-2", Guid.NewGuid(), original.Id, amount: 7m);

        Assert.Equal(PostingOutcomeKind.Posted, second.Kind);
        Assert.NotEqual(first.Journal!.Id, second.Journal!.Id);
        Assert.Equal(original.Id, second.Journal.LinkedJournalId);
        Assert.Equal(3, world.Ledger.Journals.Count);
    }

    [Fact]
    public void A_correction_id_reused_for_another_journal_is_refused()
    {
        var world = new AccountingWorld();
        var a = world.PostOriginal("m-1");
        var b = world.Ledger.Receive(world.Request("m-2", purpose: "synthetic-purpose-b"), world.OpenPeriod).Journal!;
        var correctionId = Guid.NewGuid();
        world.Correct("c-1", correctionId, a.Id);

        var error = Assert.Throws<AccountingDomainException>(() => world.Correct("c-2", correctionId, b.Id));

        Assert.Equal(AccountingErrorCodes.CorrectionIdentityConflict, error.Code);
        Assert.Equal(3, world.Ledger.Journals.Count);
        // The refused delivery was not recorded, so it can still carry a proper correction.
        Assert.Equal(PostingOutcomeKind.Posted, world.Correct("c-2", Guid.NewGuid(), b.Id).Kind);
    }

    [Fact]
    public void A_correction_needs_a_business_identity()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");

        var error = Assert.Throws<AccountingDomainException>(() => world.Correct("c-1", Guid.Empty, original.Id));

        Assert.Equal(AccountingErrorCodes.InvalidReference, error.Code);
    }

    [Fact]
    public void A_replayed_correction_message_returns_the_recorded_journal()
    {
        var world = new AccountingWorld();
        var original = world.PostOriginal("m-1");
        var first = world.Correct("c-1", Guid.NewGuid(), original.Id);

        var replay = world.Correct("c-1", Guid.NewGuid(), original.Id);

        Assert.Equal(PostingOutcomeKind.DuplicateMessage, replay.Kind);
        Assert.Same(first.Journal, replay.Journal);
        Assert.Equal(2, world.Ledger.Journals.Count);
    }

    [Fact]
    public void A_replayed_queued_message_returns_its_queue_entry_and_stays_a_duplicate_after_the_mapping_is_fixed()
    {
        var world = new AccountingWorld();
        var queued = world.Ledger.Receive(world.Request("m-1", mapCredit: false), world.OpenPeriod);

        // The same message again, now with the mapping present: still the same delivery, no posting.
        var replay = world.Ledger.Receive(world.Request("m-1"), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.DuplicateMessage, replay.Kind);
        Assert.Same(queued.Exception, replay.Exception);
        Assert.Empty(world.Ledger.Journals);
        Assert.Single(world.Ledger.OpenExceptions);
    }

    [Fact]
    public void A_mapped_but_unvalued_delivery_clears_the_stale_mapping_exception()
    {
        var world = new AccountingWorld();
        world.Ledger.Receive(world.Request("m-1", mapCredit: false), world.OpenPeriod);

        var outcome = world.Ledger.Receive(world.Request("m-2", amount: null), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.WaitingValuation, outcome.Kind);
        Assert.Empty(world.Ledger.OpenExceptions);
        Assert.Single(world.Ledger.PendingValuations);
    }

    [Fact]
    public void A_valued_but_unmapped_delivery_clears_the_stale_pending_valuation()
    {
        var world = new AccountingWorld();
        world.Ledger.Receive(world.Request("m-1", amount: null), world.OpenPeriod);

        var outcome = world.Ledger.Receive(world.Request("m-2", mapCredit: false), world.OpenPeriod);

        Assert.Equal(PostingOutcomeKind.ExceptionQueued, outcome.Kind);
        Assert.Empty(world.Ledger.PendingValuations);
        Assert.Single(world.Ledger.OpenExceptions);
    }

    [Fact]
    public void An_unmapped_and_unvalued_delivery_keeps_both_entries()
    {
        var world = new AccountingWorld();
        world.Ledger.Receive(world.Request("m-1", amount: null), world.OpenPeriod);

        world.Ledger.Receive(world.Request("m-2", amount: null, mapCredit: false), world.OpenPeriod);

        Assert.Single(world.Ledger.PendingValuations);
        Assert.Single(world.Ledger.OpenExceptions);
    }

    [Theory]
    [InlineData(" m-1")]
    [InlineData("m-1 ")]
    [InlineData("\tm-1")]
    [InlineData("")]
    [InlineData("  ")]
    public void A_message_id_with_surrounding_whitespace_is_refused_not_trimmed(string messageId)
    {
        var world = new AccountingWorld();

        var error = Assert.Throws<AccountingDomainException>(
            () => world.Ledger.Receive(world.Request(messageId), world.OpenPeriod));

        Assert.Equal(AccountingErrorCodes.InvalidReference, error.Code);
        Assert.Empty(world.Ledger.Journals);
    }

    [Theory]
    [InlineData(" consumer")]
    [InlineData("consumer ")]
    public void A_consumer_with_surrounding_whitespace_is_refused(string consumer)
    {
        var world = new AccountingWorld();

        var error = Assert.Throws<AccountingDomainException>(
            () => world.Ledger.Receive(world.Request("m-1") with { Consumer = consumer }, world.OpenPeriod));

        Assert.Equal(AccountingErrorCodes.InvalidReference, error.Code);
    }

    [Theory]
    [InlineData(" synthetic-purpose-a")]
    [InlineData("synthetic-purpose-a ")]
    public void A_posting_purpose_with_surrounding_whitespace_is_refused(string purpose)
    {
        var error = Assert.Throws<AccountingDomainException>(
            () => new SourceReference(Guid.NewGuid(), Guid.NewGuid(), purpose));

        Assert.Equal(AccountingErrorCodes.InvalidReference, error.Code);
    }

    [Fact]
    public void The_recording_instant_must_be_utc()
    {
        var error = Assert.Throws<AccountingDomainException>(() => new PostingDates(
            new DateOnly(2026, 9, 14),
            new DateOnly(2026, 9, 15),
            null,
            new DateTimeOffset(2026, 9, 18, 13, 0, 0, TimeSpan.FromHours(3))));

        Assert.Equal(AccountingErrorCodes.InvalidDates, error.Code);
    }

    [Fact]
    public void Unset_posting_dates_are_refused()
    {
        var instant = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
        var day = new DateOnly(2026, 9, 15);

        Assert.Equal(AccountingErrorCodes.InvalidDates, Assert.Throws<AccountingDomainException>(() => new PostingDates(DateOnly.MinValue, day, null, instant)).Code);
        Assert.Equal(AccountingErrorCodes.InvalidDates, Assert.Throws<AccountingDomainException>(() => new PostingDates(day, DateOnly.MinValue, null, instant)).Code);
        Assert.Equal(AccountingErrorCodes.InvalidDates, Assert.Throws<AccountingDomainException>(() => new PostingDates(day, day, DateOnly.MinValue, instant)).Code);
        Assert.Equal(AccountingErrorCodes.InvalidDates, Assert.Throws<AccountingDomainException>(() => new PostingDates(day, day, null, default)).Code);
    }

    [Fact]
    public void Closing_records_who_and_when_and_a_second_close_is_refused()
    {
        var world = new AccountingWorld();
        world.OpenPeriod.Close(AccountingWorld.Closer, AccountingWorld.ClosedAt);

        var error = Assert.Throws<AccountingDomainException>(
            () => world.OpenPeriod.Close(Guid.NewGuid(), AccountingWorld.ClosedAt.AddHours(1)));

        Assert.Equal(AccountingErrorCodes.PeriodAlreadyClosed, error.Code);
        Assert.Equal(AccountingPeriodStatus.Closed, world.OpenPeriod.Status);
        Assert.Equal(AccountingWorld.Closer, world.OpenPeriod.ClosedByUserId);
        Assert.Equal(AccountingWorld.ClosedAt, world.OpenPeriod.ClosedAtUtc);
    }

    [Fact]
    public void Closing_needs_an_actor_and_a_utc_instant()
    {
        var world = new AccountingWorld();

        Assert.Equal(AccountingErrorCodes.InvalidReference, Assert.Throws<AccountingDomainException>(() => world.OpenPeriod.Close(Guid.Empty, AccountingWorld.ClosedAt)).Code);
        Assert.Equal(AccountingErrorCodes.InvalidReference, Assert.Throws<AccountingDomainException>(() => world.OpenPeriod.Close(AccountingWorld.Closer, default)).Code);
        Assert.Equal(AccountingErrorCodes.InvalidReference, Assert.Throws<AccountingDomainException>(() => world.OpenPeriod.Close(AccountingWorld.Closer, new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.FromHours(3)))).Code);
        Assert.Equal(AccountingPeriodStatus.Open, world.OpenPeriod.Status);
        Assert.Null(world.OpenPeriod.ClosedByUserId);
    }
}
