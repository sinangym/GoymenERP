using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over Finance's pending party credit limit proposals, on SYNTHETIC rows. Only
/// Proposed records of the server-resolved company are listed, oldest first, the caller's own included (Work hides a maker's
/// own item). The rule numbers are the stored proposed amount, the stored current approved limit and their difference; the
/// source refuses without the company-wide approve permission and writes nothing.
/// </summary>
public sealed class FinancePartyCreditLimitWorkInboxSourceV1Tests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid Usd = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 9, 0, 0, TimeSpan.FromHours(3));
    private const string Caller = "SENTETIK-approver";
    private const string Proposer = "SENTETIK-proposer";

    [Fact]
    public async Task Only_proposed_records_of_the_company_are_listed_oldest_first_and_the_callers_own_proposal_is_included()
    {
        var older = Proposal(Guid.NewGuid(), "500", Start, proposedBy: Caller);
        var newer = Proposal(Guid.NewGuid(), "700", Start.AddHours(2));
        await using var run = await Run.CreateAsync(
            newer,
            older,
            Record(Guid.NewGuid(), FinancePartyCreditLimitService.Approved, "100", Start.AddMinutes(-5), revision: 1),
            Record(Guid.NewGuid(), FinancePartyCreditLimitService.Rejected, "200", Start.AddMinutes(-4)),
            Record(Guid.NewGuid(), FinancePartyCreditLimitService.Superseded, "300", Start.AddMinutes(-3)),
            Proposal(Guid.NewGuid(), "900", Start.AddMinutes(-10), company: OtherCompany));

        var result = await run.Source().ListPendingAsync(Context(), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Equal(Company, result.CompanyId);
        Assert.False(result.HasMore);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal((FinancePartyCreditLimitWorkInboxSourceV1.ModuleName, FinancePartyCreditLimitWorkInboxSourceV1.KindName),
            (first.Module, first.Kind));
        Assert.Equal(Caller, first.PreparedBy);
        Assert.Equal(older.CompanyAccountId.ToString("D")[..8], first.DocumentLabel);
        Assert.Equal(Start.ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal(TimeSpan.Zero, first.WaitingSinceUtc.Offset);
        Assert.Equal(FinancePartyCreditLimitWorkInboxSourceV1.ScreenPath, first.Link);
        Assert.True(NativeApprovalQueueRulesV1.IsValidLink(first.Link));
        Assert.All(result.Items!, item => Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule("finance", rule))));
        Assert.All(result.Items!, item => Assert.All(item.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference))));
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Another_companys_proposals_never_appear_and_its_own_company_sees_only_its_own()
    {
        var mine = Proposal(Guid.NewGuid(), "500", Start);
        var theirs = Proposal(Guid.NewGuid(), "600", Start.AddMinutes(-30), company: OtherCompany);
        await using var run = await Run.CreateAsync(mine, theirs);

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
        await using var run = await Run.CreateAsync(Proposal(Guid.NewGuid(), "500", Start));
        var authority = new FixedAuthority(granted, companyWide);

        var result = await run.Source(Company, authority).ListPendingAsync(
            new WorkInboxSourceContextV1(Company, contextUser, "SENTETIK-correlation", null), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Items);
        Assert.All(authority.Asked, asked => Assert.Equal(
            (ErpPermissions.FinanceCreditLimitsApprove, Company), asked));
    }

    [Fact]
    public async Task A_company_context_other_than_the_callers_is_denied_before_any_authority_is_asked()
    {
        await using var run = await Run.CreateAsync(Proposal(Guid.NewGuid(), "500", Start));
        var authority = new FixedAuthority(true, true);

        var result = await run.Source(Company, authority).ListPendingAsync(Context(OtherCompany), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Empty(authority.Asked);
    }

    [Fact]
    public async Task More_pending_proposals_than_asked_set_has_more_and_return_the_oldest()
    {
        var rows = Enumerable.Range(0, 3)
            .Select(index => Proposal(Guid.NewGuid(), "500", Start.AddMinutes(index)))
            .ToArray();
        await using var run = await Run.CreateAsync(rows);

        var result = await run.Source().ListPendingAsync(Context(), 2, CancellationToken.None);

        Assert.True(result.HasMore);
        Assert.Equal(rows.Take(2).Select(row => row.Id), result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task The_rule_numbers_are_the_stored_current_limit_the_stored_proposed_amount_and_their_difference()
    {
        var changeAccount = Guid.NewGuid();
        var newAccount = Guid.NewGuid();
        var removeAccount = Guid.NewGuid();
        var currencyAccount = Guid.NewGuid();
        var staleAccount = Guid.NewGuid();
        var change = Proposal(changeAccount, "1250.50", Start, baseRevision: 1);
        var fresh = Proposal(newAccount, "300", Start.AddMinutes(1));
        var removal = Proposal(removeAccount, null, Start.AddMinutes(2), kind: FinancePartyCreditLimitService.RemoveKind,
            baseRevision: 1);
        var currency = Proposal(currencyAccount, "40", Start.AddMinutes(3), currency: Usd, baseRevision: 1);
        var stale = Proposal(staleAccount, "800", Start.AddMinutes(4), baseRevision: 1);
        await using var run = await Run.CreateAsync(
            change, fresh, removal, currency, stale,
            Record(changeAccount, FinancePartyCreditLimitService.Approved, "1000.25", Start.AddDays(-1), revision: 1),
            Record(removeAccount, FinancePartyCreditLimitService.Approved, "75", Start.AddDays(-1), revision: 1),
            Record(currencyAccount, FinancePartyCreditLimitService.Approved, "900", Start.AddDays(-1), revision: 1),
            Record(staleAccount, FinancePartyCreditLimitService.Superseded, "100", Start.AddDays(-2), revision: 1),
            Record(staleAccount, FinancePartyCreditLimitService.Approved, "200", Start.AddDays(-1), revision: 2));

        var result = await run.Source().ListPendingAsync(Context(), 10, CancellationToken.None);

        var items = result.Items!.ToDictionary(item => item.ItemId);
        Assert.Equal([new WorkApprovalRuleResultV1(FinancePartyCreditLimitWorkInboxSourceV1.ChangeRule, "1000.25", "1250.5", "250.25", null)],
            items[change.Id].Rules);
        Assert.Equal([new WorkApprovalRuleResultV1(FinancePartyCreditLimitWorkInboxSourceV1.NewRule, null, "300", null, null)],
            items[fresh.Id].Rules);
        Assert.Equal([new WorkApprovalRuleResultV1(FinancePartyCreditLimitWorkInboxSourceV1.RemovalRule, "75", null, null, null)],
            items[removal.Id].Rules);
        Assert.Equal([new WorkApprovalRuleResultV1(FinancePartyCreditLimitWorkInboxSourceV1.CurrencyChangeRule, null, "40", null, null)],
            items[currency.Id].Rules);
        Assert.Contains(items[currency.Id].References, reference => reference.Role == "currency" && reference.RecordId == Usd);
        Assert.Contains(items[currency.Id].References, reference => reference.Role == "current-currency" && reference.RecordId == Try);
        Assert.Equal(
            [
                new WorkApprovalRuleResultV1(FinancePartyCreditLimitWorkInboxSourceV1.ChangeRule, "200", "800", "600", null),
                new WorkApprovalRuleResultV1(FinancePartyCreditLimitWorkInboxSourceV1.RevisionStaleRule, null, null, null, null),
            ],
            items[stale.Id].Rules);
        Assert.Contains(items[change.Id].References,
            reference => reference.Role == "company-account" && reference.RecordId == changeAccount);
    }

    private static WorkInboxSourceContextV1 Context(Guid? company = null) =>
        new(company ?? Company, Caller, "SENTETIK-correlation", null);

    private static PartyCreditLimitRow Proposal(Guid account, string? amount, DateTimeOffset at, string proposedBy = Proposer,
        Guid? company = null, int kind = FinancePartyCreditLimitService.SetKind, Guid? currency = null, long baseRevision = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company ?? Company,
            PartyId = Guid.NewGuid(),
            CompanyAccountId = account,
            Kind = kind,
            CurrencyId = kind == FinancePartyCreditLimitService.SetKind ? currency ?? Try : null,
            AmountText = amount,
            Status = FinancePartyCreditLimitService.Proposed,
            BaseRevision = baseRevision,
            Version = 1,
            ProposedBy = proposedBy,
            ProposedAtUtc = at,
            UpdatedAtUtc = at,
        };

    private static PartyCreditLimitRow Record(Guid account, int status, string amount, DateTimeOffset at, long? revision = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = Company,
            PartyId = Guid.NewGuid(),
            CompanyAccountId = account,
            Kind = FinancePartyCreditLimitService.SetKind,
            CurrencyId = Try,
            AmountText = amount,
            Status = status,
            IsDirect = true,
            AccountRevision = revision,
            Version = 1,
            ProposedBy = Proposer,
            ProposedAtUtc = at,
            DecidedBy = status == FinancePartyCreditLimitService.Proposed ? null : Caller,
            DecidedAtUtc = status == FinancePartyCreditLimitService.Proposed ? null : at,
            UpdatedAtUtc = at,
        };

    private sealed class Run : IAsyncDisposable
    {
        private readonly string _database = $"credit-limit-inbox-{Guid.NewGuid():N}";
        public FinanceDbContext Db { get; private set; } = null!;

        public static async Task<Run> CreateAsync(params PartyCreditLimitRow[] rows)
        {
            var run = new Run();
            foreach (var company in rows.Select(row => row.CompanyId).Append(Company).Distinct())
            {
                var seed = new CompanyContext();
                seed.Set(company);
                await using var db = run.Context(seed);
                db.PartyCreditLimits.AddRange(rows.Where(row => row.CompanyId == company));
                await db.SaveChangesAsync();
            }

            return run;
        }

        public FinancePartyCreditLimitWorkInboxSourceV1 Source(Guid? company = null, FixedAuthority? authority = null)
        {
            var context = new CompanyContext();
            context.Set(company ?? Company);
            Db = Context(context);
            return new FinancePartyCreditLimitWorkInboxSourceV1(Db, new FinanceWorkInboxFence(context,
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

    internal sealed class FixedAuthority(bool granted, bool companyWide) : IErpAuthorityRevalidationService
    {
        public List<(string Permission, Guid Company)> Asked { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((permission, companyId));
            return Task.FromResult(new PermissionScope(granted, companyWide, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    internal sealed class FixedUser(string userId, Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>(companies);
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
