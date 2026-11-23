using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using FixedAuthority = GoymenERP.Finance.UnitTests.FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedAuthority;
using FixedUser = GoymenERP.Finance.UnitTests.FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedUser;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32, D4, D11, YOL-D23): the facts every Finance Work inbox source must hold, run once per source on
/// SYNTHETIC rows. Only the owner's pending records of the server-resolved company are listed, oldest first, the caller's
/// own included (Work hides a maker's own item); another company never appears; the source refuses without the
/// company-wide approve permission or for another actor; the maker the owner refuses is the item's preparer and no one
/// else is excluded; HasMore holds; a row that breaks the owner's invariants makes the answer Unavailable; the rules and
/// references fit the contract; nothing is written; scoped approvers are not accepted.
/// </summary>
public abstract class FinanceWorkInboxSourceContractTests
{
    protected static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    protected static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    protected static readonly DateTimeOffset Start = new(2026, 11, 20, 9, 0, 0, TimeSpan.FromHours(3));
    protected const string Caller = "SENTETIK-approver";
    protected const string Maker = "SENTETIK-maker";

    private protected abstract string ExpectedKind { get; }

    private protected abstract string ExpectedPermission { get; }

    private protected abstract IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence);

    /// <summary>Adds one record the owner's decision command accepts as pending; returns the expected item id.</summary>
    private protected abstract Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker);

    /// <summary>Adds records of the company the owner's decision command does not accept as pending.</summary>
    private protected abstract void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at);

    /// <summary>Adds one pending record that breaks the owner's invariants.</summary>
    private protected abstract void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at);

    [Fact]
    public async Task Only_pending_records_of_the_company_are_listed_oldest_first_and_the_callers_own_is_included()
    {
        await using var run = new Run(this);
        var newer = await run.SeedAsync(Company, db => AddPending(db, Company, Start.AddHours(1), Maker));
        var older = await run.SeedAsync(Company, db => AddPending(db, Company, Start, Caller));
        await run.SeedAsync(Company, db => { AddNotPending(db, Company, Start.AddDays(-1)); return Guid.Empty; });
        await run.SeedAsync(OtherCompany, db => AddPending(db, OtherCompany, Start.AddDays(-2), Maker));

        var result = await run.Source().ListPendingAsync(Context(), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Equal(Company, result.CompanyId);
        Assert.False(result.HasMore);
        Assert.Equal([older, newer], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(("finance", ExpectedKind), (first.Module, first.Kind));
        Assert.Equal(Caller, first.PreparedBy);
        Assert.Equal(Start.ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal(TimeSpan.Zero, first.WaitingSinceUtc.Offset);
        AssertContract(result);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Another_companys_records_never_appear_and_its_own_company_sees_only_its_own()
    {
        await using var run = new Run(this);
        var mine = await run.SeedAsync(Company, db => AddPending(db, Company, Start, Maker));
        var theirs = await run.SeedAsync(OtherCompany, db => AddPending(db, OtherCompany, Start.AddMinutes(-30), Maker));

        var result = await run.Source().ListPendingAsync(Context(), 10, CancellationToken.None);
        var other = await run.Source(OtherCompany).ListPendingAsync(Context(OtherCompany), 10, CancellationToken.None);

        Assert.Equal([mine], result.Items!.Select(item => item.ItemId));
        Assert.Equal([theirs], other.Items!.Select(item => item.ItemId));
    }

    [Theory]
    [InlineData(false, true, Caller)]
    [InlineData(true, false, Caller)]
    [InlineData(true, true, "SENTETIK-someone-else")]
    public async Task Without_the_company_wide_approve_permission_or_with_another_actor_the_source_is_denied(
        bool granted, bool companyWide, string contextUser)
    {
        await using var run = new Run(this);
        await run.SeedAsync(Company, db => AddPending(db, Company, Start, Maker));
        var authority = new FixedAuthority(granted, companyWide);

        var result = await run.Source(Company, authority).ListPendingAsync(
            new WorkInboxSourceContextV1(Company, contextUser, "SENTETIK-correlation", null), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Items);
        Assert.All(authority.Asked, asked => Assert.Equal((ExpectedPermission, Company), asked));
    }

    [Fact]
    public async Task A_company_context_other_than_the_callers_is_denied_before_any_authority_is_asked()
    {
        await using var run = new Run(this);
        await run.SeedAsync(Company, db => AddPending(db, Company, Start, Maker));
        var authority = new FixedAuthority(true, true);

        var result = await run.Source(Company, authority).ListPendingAsync(Context(OtherCompany), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Empty(authority.Asked);
    }

    [Fact]
    public async Task The_approver_permission_is_the_owners_and_scoped_approvers_are_not_accepted()
    {
        await using var run = new Run(this);
        var source = run.Source();

        Assert.Equal(ExpectedPermission, source.ApproverPermission);
        Assert.True(WorkInboxRulesV1.IsValidPermission(source.ApproverPermission));
        Assert.Equal(ExpectedKind, source.Kind);
        Assert.True(NativeApprovalQueueRulesV1.IsValidKind("finance", source.Kind));
        Assert.False(source.AcceptsScopedApprovers);
    }

    [Fact]
    public async Task The_maker_the_owner_refuses_is_the_preparer_and_no_one_else_is_excluded()
    {
        await using var run = new Run(this);
        await run.SeedAsync(Company, db => AddPending(db, Company, Start, Maker));

        var result = await run.Source().ListPendingAsync(Context(), 10, CancellationToken.None);

        var item = Assert.Single(result.Items!);
        Assert.Equal(Maker, item.PreparedBy);
        Assert.Null(item.ExcludedDeciders);
        Assert.True(item.Excludes(Maker));
        Assert.False(item.Excludes(Caller));
        Assert.Null(item.Scope);
    }

    [Fact]
    public async Task More_pending_records_than_asked_set_has_more_and_return_the_oldest()
    {
        await using var run = new Run(this);
        var ids = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            var at = Start.AddMinutes(index);
            ids.Add(await run.SeedAsync(Company, db => AddPending(db, Company, at, Maker)));
        }

        var result = await run.Source().ListPendingAsync(Context(), 2, CancellationToken.None);

        Assert.True(result.HasMore);
        Assert.Equal(ids.Take(2), result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task A_record_that_breaks_the_owners_invariants_makes_the_answer_unavailable_never_a_partial_list()
    {
        await using var run = new Run(this);
        await run.SeedAsync(Company, db => AddPending(db, Company, Start.AddMinutes(-5), Maker));
        await run.SeedAsync(Company, db => { AddInvalid(db, Company, Start); return Guid.Empty; });

        var result = await run.Source().ListPendingAsync(Context(), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Items);
    }

    protected static WorkInboxSourceContextV1 Context(Guid? company = null) =>
        new(company ?? Company, Caller, "SENTETIK-correlation", null);

    protected static void AssertContract(WorkInboxSourceResultV1 result)
    {
        Assert.All(result.Items!, item =>
        {
            Assert.True(NativeApprovalQueueRulesV1.IsValidLink(item.Link));
            Assert.InRange(item.DocumentLabel.Length, 1, WorkInboxRulesV1.MaxDocumentLabelLength);
            Assert.NotEmpty(item.Rules);
            Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule("finance", rule)));
            Assert.All(item.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference)));
        });
    }

    /// <summary>Lists the company's pending items with a granted company-wide authority, whatever the outcome.</summary>
    private protected async Task<WorkInboxSourceResultV1> ListAnyAsync(Action<FinanceDbContext> seed)
    {
        await using var run = new Run(this);
        await run.SeedAsync(Company, db => { seed(db); return Guid.Empty; });
        return await run.Source().ListPendingAsync(Context(), 10, CancellationToken.None);
    }

    /// <summary>Lists the company's pending items with a granted company-wide authority (for the per-source facts).</summary>
    private protected async Task<WorkInboxSourceResultV1> ListAsync(Action<FinanceDbContext> seed)
    {
        await using var run = new Run(this);
        await run.SeedAsync(Company, db => { seed(db); return Guid.Empty; });
        var result = await run.Source().ListPendingAsync(Context(), 10, CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        AssertContract(result);
        Assert.False(run.Db.ChangeTracker.HasChanges());
        return result;
    }

    private sealed class Run(FinanceWorkInboxSourceContractTests owner) : IAsyncDisposable
    {
        private readonly string _database = $"finance-work-inbox-{Guid.NewGuid():N}";
        public FinanceDbContext Db { get; private set; } = null!;

        public async Task<Guid> SeedAsync(Guid company, Func<FinanceDbContext, Guid> add)
        {
            var seed = new CompanyContext();
            seed.Set(company);
            await using var db = Context(seed);
            var id = add(db);
            await db.SaveChangesAsync();
            return id;
        }

        public IWorkInboxSourceV1 Source(Guid? company = null, FixedAuthority? authority = null)
        {
            var context = new CompanyContext();
            context.Set(company ?? Company);
            Db = Context(context);
            return owner.Create(Db, new FinanceWorkInboxFence(context,
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
