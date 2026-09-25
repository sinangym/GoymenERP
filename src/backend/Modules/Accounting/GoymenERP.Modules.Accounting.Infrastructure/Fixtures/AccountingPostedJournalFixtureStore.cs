using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Accounting.Infrastructure.Fixtures;

/// <summary>
/// Explicit test-only port for ONE synthetic posted journal. The host registers it only behind the existing complete
/// browser fixture gate, beside <see cref="IAccountingOpenPeriodFixtureStore"/>, whose period it posts into.
/// </summary>
public interface IAccountingPostedJournalFixtureStore
{
    Task EnsureAsync(CancellationToken cancellationToken);
}

public static class AccountingPostedJournalFixtureStoreRegistration
{
    /// <summary>
    /// Registers only this store. It needs <see cref="IAccountingOpenPeriodFixtureStore"/> (registered by
    /// <c>AddAccountingOpenPeriodFixtureStore</c>) and the module's posting store, so the gated host calls both.
    /// </summary>
    public static IServiceCollection AddAccountingPostedJournalFixtureStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IAccountingPostedJournalFixtureStore, AccountingPostedJournalFixtureStore>();
        return services;
    }
}

/// <summary>
/// Fixed SENTETIK acc01 posted journal (ACC-V1 reversal slice). NOT UAT, NOT a real source record, NOT a real account:
/// the source id, the posting purpose/rule labels and both account references are synthetic and unmapped. It exists
/// only so a browser suite can open a journal detail, propose its reversal and read M-06 over real rows.
/// <para>
/// It is posted into the fixed fin01 Open period (<see cref="AccountingOpenPeriodFixture"/>) of GDC, at a date inside
/// that period and inside the approved GDC TRY money profile (effective 2026-09-18, scale 2).
/// </para>
/// </summary>
public static class AccountingPostedJournalFixture
{
    public static readonly Guid CompanyId = AccountingOpenPeriodFixture.CompanyId;
    public static readonly Guid PeriodId = AccountingOpenPeriodFixture.PeriodId;
    public static readonly Guid JournalId = Guid.Parse("b7000000-0000-4000-8000-000000000601");
    public static readonly Guid SourceId = Guid.Parse("b7000000-0000-4000-8000-000000000602");
    public static readonly Guid DebitAccountId = Guid.Parse("b7000000-0000-4000-8000-000000000603");
    public static readonly Guid CreditAccountId = Guid.Parse("b7000000-0000-4000-8000-000000000604");
    public static readonly Guid CurrencyId = SettingsCurrencyIdentitiesV1.TurkishLira;
    public const string Consumer = "fixture.acc01.posted-journal.v1";
    public const string MessageId = "SENTETIK-acc01-posted-journal-1";
    public const string PostingPurpose = "sentetik-acc01-fixture";
    public const string PostingRuleVersion = "sentetik-acc01-rule-1";
    public const decimal Amount = 1250.50m;
    public static readonly DateOnly TransactionDate = new DateOnly(2026, 9, 21);
    public static readonly DateOnly AccountingDate = new DateOnly(2026, 9, 22);
    public static readonly DateTimeOffset RecordedAtUtc = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The synthetic fixture actor; it is not an Identity user and holds no grant.</summary>
    public static readonly Guid FixtureActorId = Guid.Parse("b2000000-0000-4000-8000-00000000f601");
    public const string FixturePermissionCode = "fixture.acc01.posted-journal";
    public const string FixtureRoleCode = "SENTETIK-ACC01-FIXTURE";

    public static PostingRequest Request() => new(
        Consumer,
        MessageId,
        JournalId,
        new SourceReference(CompanyId, SourceId, PostingPurpose),
        PostingRuleVersion,
        CurrencyId,
        new PostingDates(TransactionDate, AccountingDate, null, RecordedAtUtc),
        [
            new ProposedLine("sentetik-debit", new AccountRef(CompanyId, DebitAccountId), PostingSide.Debit, Amount),
            new ProposedLine("sentetik-credit", new AccountRef(CompanyId, CreditAccountId), PostingSide.Credit, Amount),
        ]);

    public static AccountingCommandMetadata Metadata() => new(
        FixtureActorId.ToString("D"),
        FixturePermissionCode,
        [FixtureRoleCode],
        "sentetik-acc01-posted-journal",
        null,
        MessageId,
        "Fixture");
}

/// <summary>
/// Posts the fixed journal through Accounting's OWN posting path (<see cref="IAccountingPostingStore.ReceiveAsync"/>):
/// the amount-scale check, the Open period, the balanced ledger rule, the delivery row and the audit event all apply.
/// The only bypass is the one the open-period fixture already makes (the period is stored Open by its fixture); no
/// journal, line or audit row is written by hand. Re-running is a replay of the same delivery (same consumer,
/// message and content): it writes nothing and answers the same journal; any other content is refused.
/// </summary>
internal sealed class AccountingPostedJournalFixtureStore(
    AccountingDbContext dbContext,
    ICompanyContext companyContext,
    IAccountingOpenPeriodFixtureStore openPeriod,
    IAccountingPostingStore postings) : IAccountingPostedJournalFixtureStore
{
    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (dbContext.ChangeTracker.Entries().Any())
        {
            throw new InvalidOperationException("An Accounting posted-journal fixture requires a clean context.");
        }

        if (companyContext.IsResolved && companyContext.CompanyId != AccountingPostedJournalFixture.CompanyId)
        {
            throw Conflict();
        }

        // The open-period fixture binds the company when it is unbound and refuses any other calendar.
        await openPeriod.EnsureAsync(cancellationToken);
        if (!companyContext.IsResolved || companyContext.CompanyId != AccountingPostedJournalFixture.CompanyId)
        {
            throw Conflict();
        }

        // The period fixture read its rows through the same scoped context; the posting path starts clean.
        dbContext.ChangeTracker.Clear();
        var outcome = await postings.ReceiveAsync(
            AccountingPostedJournalFixture.Request(),
            AccountingPostedJournalFixture.PeriodId,
            AccountingPostedJournalFixture.Metadata(),
            cancellationToken);
        // First run: Posted. Re-run: the stored delivery answers DuplicateMessage with the same journal.
        if (outcome.Kind is not (PostingOutcomeKind.Posted or PostingOutcomeKind.DuplicateMessage)
            || outcome.Journal is not { } journal
            || journal.Id != AccountingPostedJournalFixture.JournalId
            || journal.Kind != JournalKind.Original)
        {
            throw Conflict();
        }
    }

    private static InvalidOperationException Conflict() =>
        new InvalidOperationException("The Accounting ledger is not the identical synthetic acc01 posted-journal fixture.");
}
