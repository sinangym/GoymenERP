using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using FixedAuthority = GoymenERP.Finance.UnitTests.FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedAuthority;
using FixedUser = GoymenERP.Finance.UnitTests.FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedUser;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over Finance's R47 manual outgoing bank payments waiting for approval, on
/// SYNTHETIC rows. Only Draft payments of the server-resolved company are listed, oldest first, the caller's own included
/// (Work hides a maker's own item). The one rule carries the stored amount as the actual value; no threshold exists. The
/// source refuses without the company-wide approve permission and writes nothing.
/// </summary>
public sealed class FinanceManualBankPaymentWorkInboxSourceV1Tests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Bank = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 9, 0, 0, TimeSpan.FromHours(3));
    private const string Caller = "SENTETIK-approver";
    private const string Preparer = "SENTETIK-preparer";

    [Fact]
    public async Task Only_draft_payments_of_the_company_are_listed_oldest_first_and_the_callers_own_draft_is_included()
    {
        var older = Payment("100.00", Start, preparedBy: Caller);
        var newer = Payment("250.75", Start.AddHours(1));
        await using var run = await Run.CreateAsync(
            [newer, older,
                Payment("10", Start.AddDays(-1), status: ManualBankPaymentStatuses.Approved),
                Payment("20", Start.AddDays(-1), status: ManualBankPaymentStatuses.Recorded),
                Payment("30", Start.AddDays(-2), company: OtherCompany)],
            [BankAccount(Bank, "SENTETIK Banka Vadesiz")]);

        var result = await run.Source().ListPendingAsync(Context(), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Equal(Company, result.CompanyId);
        Assert.False(result.HasMore);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal((FinanceManualBankPaymentWorkInboxSourceV1.ModuleName, FinanceManualBankPaymentWorkInboxSourceV1.KindName),
            (first.Module, first.Kind));
        Assert.Equal(Caller, first.PreparedBy);
        Assert.Equal(older.Id.ToString("D")[..8], first.DocumentLabel);
        Assert.Equal(Start.ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal(TimeSpan.Zero, first.WaitingSinceUtc.Offset);
        Assert.Equal("/finans/odemeler/" + older.Id.ToString("D"), first.Link);
        Assert.True(NativeApprovalQueueRulesV1.IsValidLink(first.Link));
        Assert.Contains(first.References, reference => reference.Role == "bank-account"
            && reference.Label == "SENTETIK Banka Vadesiz" && reference.RecordId == Bank);
        Assert.All(result.Items!, item => Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule("finance", rule))));
        Assert.All(result.Items!, item => Assert.All(item.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference))));
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Another_companys_drafts_never_appear_and_its_own_company_sees_only_its_own()
    {
        var mine = Payment("100", Start);
        var theirs = Payment("200", Start.AddMinutes(-30), company: OtherCompany);
        await using var run = await Run.CreateAsync([mine, theirs], []);

        var result = await run.Source().ListPendingAsync(Context(), 10, CancellationToken.None);
        var other = await run.Source(OtherCompany).ListPendingAsync(Context(OtherCompany), 10, CancellationToken.None);

        Assert.Equal([mine.Id], result.Items!.Select(item => item.ItemId));
        Assert.Equal([theirs.Id], other.Items!.Select(item => item.ItemId));
    }

    [Theory]
    [InlineData(false, true, Caller)]
    [InlineData(true, false, Caller)]
    [InlineData(true, true, "SENTETIK-someone-else")]
    public async Task Without_the_company_wide_approve_permission_or_with_another_actor_the_source_is_denied(
        bool granted, bool companyWide, string contextUser)
    {
        await using var run = await Run.CreateAsync([Payment("100", Start)], []);
        var authority = new FixedAuthority(granted, companyWide);

        var result = await run.Source(Company, authority).ListPendingAsync(
            new WorkInboxSourceContextV1(Company, contextUser, "SENTETIK-correlation", null), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Items);
        Assert.All(authority.Asked, asked => Assert.Equal(
            (FinancePermissionNames.ManualBankPaymentApprove, Company), asked));
    }

    [Fact]
    public async Task More_drafts_than_asked_set_has_more_and_return_the_oldest()
    {
        var rows = Enumerable.Range(0, 3).Select(index => Payment("100", Start.AddMinutes(index))).ToArray();
        await using var run = await Run.CreateAsync(rows, []);

        var result = await run.Source().ListPendingAsync(Context(), 2, CancellationToken.None);

        Assert.True(result.HasMore);
        Assert.Equal(rows.Take(2).Select(row => row.Id), result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task The_rule_carries_the_stored_amount_as_actual_with_no_threshold_and_the_currency_as_a_reference()
    {
        var document = Guid.NewGuid();
        var payment = Payment("1234.5600", Start, linkedDocument: document);
        await using var run = await Run.CreateAsync([payment], []);

        var result = await run.Source().ListPendingAsync(Context(), 10, CancellationToken.None);

        var item = Assert.Single(result.Items!);
        Assert.Equal(
            [new WorkApprovalRuleResultV1(FinanceManualBankPaymentWorkInboxSourceV1.ApprovalRequiredRule, null, "1234.56", null, null)],
            item.Rules);
        Assert.Contains(item.References, reference => reference.Role == "currency" && reference.RecordId == Try);
        Assert.Contains(item.References, reference => reference.Role == "document" && reference.RecordId == document);
        // A bank account Finance does not find is still shown by its short reference, never dropped.
        Assert.Contains(item.References, reference => reference.Role == "bank-account" && reference.Label == Bank.ToString("D")[..8]);
    }

    private static WorkInboxSourceContextV1 Context(Guid? company = null) =>
        new(company ?? Company, Caller, "SENTETIK-correlation", null);

    private static ManualBankPaymentRow Payment(string amount, DateTimeOffset at, string preparedBy = Preparer,
        int status = ManualBankPaymentStatuses.Draft, Guid? company = null, Guid? linkedDocument = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company ?? Company,
            Version = 1,
            PartyId = Guid.NewGuid(),
            CompanyAccountId = Guid.NewGuid(),
            TreasuryAccountId = Bank,
            CurrencyId = Try,
            AmountText = amount,
            ProfileId = "SENTETIK-profile",
            ProfileVersion = "1",
            OccurredOn = new DateOnly(2026, 11, 20),
            LinkedDocumentId = linkedDocument,
            LinkedDocumentVersion = linkedDocument is null ? null : "1",
            Status = status,
            PreparedBy = preparedBy,
            PreparedAtUtc = at,
            ApprovedBy = status == ManualBankPaymentStatuses.Draft ? null : Caller,
            ApprovedAtUtc = status == ManualBankPaymentStatuses.Draft ? null : at,
        };

    private static BankAccountRow BankAccount(Guid id, string name) =>
        new()
        {
            Id = id,
            CompanyId = Company,
            CurrencyId = Try,
            DisplayName = name,
            Status = 2,
            Version = 1,
            ProposedBy = Preparer,
            ProposedAtUtc = Start,
            UpdatedAtUtc = Start,
        };

    private sealed class Run : IAsyncDisposable
    {
        private readonly string _database = $"manual-bank-payment-inbox-{Guid.NewGuid():N}";
        public FinanceDbContext Db { get; private set; } = null!;

        public static async Task<Run> CreateAsync(ManualBankPaymentRow[] payments, BankAccountRow[] banks)
        {
            var run = new Run();
            foreach (var company in payments.Select(row => row.CompanyId).Append(Company).Distinct())
            {
                var seed = new CompanyContext();
                seed.Set(company);
                await using var db = run.Context(seed);
                db.ManualBankPayments.AddRange(payments.Where(row => row.CompanyId == company));
                db.BankAccounts.AddRange(banks.Where(row => row.CompanyId == company));
                await db.SaveChangesAsync();
            }

            return run;
        }

        public FinanceManualBankPaymentWorkInboxSourceV1 Source(Guid? company = null, FixedAuthority? authority = null)
        {
            var context = new CompanyContext();
            context.Set(company ?? Company);
            Db = Context(context);
            return new FinanceManualBankPaymentWorkInboxSourceV1(Db, new FinanceWorkInboxFence(context,
                new FixedUser(Caller, [Company, OtherCompany]), authority ?? new FixedAuthority(true, true)));
        }

        private FinanceDbContext Context(ICompanyContext company) => new(
            new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(_database).Options, company);

        public async ValueTask DisposeAsync()
        {
            if (Db is not null)
            {
                await Db.DisposeAsync();
            }
        }
    }
}
