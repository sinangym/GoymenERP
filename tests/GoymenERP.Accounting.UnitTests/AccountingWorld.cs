using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// Synthetic identities only. No real chart of accounts, currency, rule version or period is
/// represented here; every value is a test identity.
/// </summary>
internal sealed class AccountingWorld
{
    public const string Consumer = "synthetic-accounting-consumer";
    public const string Purpose = "synthetic-purpose-a";

    public static readonly Guid Closer = Guid.NewGuid();
    public static readonly DateTimeOffset ClosedAt = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public Guid Company { get; } = Guid.NewGuid();
    public Guid OtherCompany { get; } = Guid.NewGuid();
    public Guid Currency { get; } = Guid.NewGuid();
    public Guid SourceId { get; } = Guid.NewGuid();
    public AccountRef DebitAccount { get; }
    public AccountRef CreditAccount { get; }
    public AccountingPeriod OpenPeriod { get; }
    public SourcePostingLedger Ledger { get; }

    public AccountingWorld()
    {
        DebitAccount = new AccountRef(Company, Guid.NewGuid());
        CreditAccount = new AccountRef(Company, Guid.NewGuid());
        OpenPeriod = new AccountingPeriod(Guid.NewGuid(), Company, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Ledger = new SourcePostingLedger(Company);
    }

    public static PostingDates Dates(DateOnly accounting) =>
        new(accounting.AddDays(-1), accounting, null, new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero));

    public PostingDates InPeriod => Dates(new DateOnly(2026, 9, 15));

    public SourceReference Source(string purpose = Purpose) => new(Company, SourceId, purpose);

    public PostingRequest Request(
        string messageId,
        decimal? amount = 100m,
        string ruleVersion = "synthetic-rule-1",
        string purpose = Purpose,
        bool mapCredit = true,
        Guid? journalId = null) =>
        new(
            Consumer,
            messageId,
            journalId ?? Guid.NewGuid(),
            Source(purpose),
            ruleVersion,
            Currency,
            InPeriod,
            [
                new ProposedLine("synthetic-debit-key", DebitAccount, PostingSide.Debit, amount),
                new ProposedLine("synthetic-credit-key", mapCredit ? CreditAccount : null, PostingSide.Credit, amount),
            ]);

    public PostingOutcome Correct(string messageId, Guid correctionId, Guid correctedJournalId, decimal amount = 5m) =>
        Ledger.Correct(
            Consumer,
            messageId,
            correctionId,
            correctedJournalId,
            Guid.NewGuid(),
            "synthetic-rule-2",
            InPeriod,
            [new JournalLine(DebitAccount, amount, 0m), new JournalLine(CreditAccount, 0m, amount)],
            OpenPeriod);

    public Journal PostOriginal(string messageId = "m-original", decimal amount = 100m)
    {
        var outcome = Ledger.Receive(Request(messageId, amount), OpenPeriod);
        Assert.Equal(PostingOutcomeKind.Posted, outcome.Kind);
        return outcome.Journal!;
    }
}
