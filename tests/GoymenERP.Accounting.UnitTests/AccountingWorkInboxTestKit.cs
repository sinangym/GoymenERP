using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// W1-A test kit for the Accounting Work inbox sources, on SYNTHETIC rows in an in-memory Accounting database. Rows are
/// seeded per company under that company's resolved context (the owner's write guard), and every source reads through a
/// fresh context so the tracker shows only what the source itself did.
/// </summary>
internal sealed class AccountingWorkInboxTestKit : IAsyncDisposable
{
    public static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid Caller = Guid.Parse("a1a1a1a1-0000-4000-8000-00000000c411");
    public static readonly Guid Maker = Guid.Parse("b2b2b2b2-0000-4000-8000-0000000004a3");
    public static readonly DateTimeOffset Start = new(2026, 11, 20, 9, 0, 0, TimeSpan.FromHours(3));
    public const string SyntheticReason = "SENTETIK-gerekce";

    private readonly string _database = $"accounting-work-inbox-{Guid.NewGuid():N}";

    public AccountingDbContext Db { get; private set; } = null!;

    public static string CallerText => Caller.ToString("D");

    public async Task SeedAsync(Guid company, Action<AccountingDbContext> add)
    {
        var seed = new CompanyContext();
        seed.Set(company);
        await using var db = NewContext(seed);
        add(db);
        await db.SaveChangesAsync();
    }

    /// <summary>A fresh read context for <paramref name="company"/> and the fence the source applies.</summary>
    public AccountingWorkInboxFence Fence(Guid? company = null, FixedAuthority? authority = null)
    {
        var context = new CompanyContext();
        context.Set(company ?? Company);
        Db = NewContext(context);
        return new AccountingWorkInboxFence(context, new FixedUser(CallerText, [Company, OtherCompany]),
            authority ?? new FixedAuthority(true, true));
    }

    public static WorkInboxSourceContextV1 Context(Guid? company = null, string? user = null) =>
        new(company ?? Company, user ?? CallerText, "SENTETIK-correlation", null);

    /// <summary>The contract's shape rules every listed item must satisfy.</summary>
    public static void AssertContractShape(WorkInboxSourceResultV1 result, string kind)
    {
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.All(result.Items!, item =>
        {
            Assert.Equal(("accounting", kind), (item.Module, item.Kind));
            Assert.NotEqual(Guid.Empty, item.ItemId);
            Assert.InRange(item.DocumentLabel.Length, 1, WorkInboxRulesV1.MaxDocumentLabelLength);
            Assert.Equal(TimeSpan.Zero, item.WaitingSinceUtc.Offset);
            Assert.True(NativeApprovalQueueRulesV1.IsValidLink(item.Link), item.Link);
            Assert.InRange(item.Rules.Count, 1, WorkInboxRulesV1.MaxRulesPerItem);
            Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule("accounting", rule), rule.RuleCode));
            Assert.InRange(item.References.Count, 0, WorkInboxRulesV1.MaxReferencesPerItem);
            Assert.All(item.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference), reference.Role));
            Assert.Null(item.Scope);
        });
    }

    private AccountingDbContext NewContext(ICompanyContext company) => new(
        new DbContextOptionsBuilder<AccountingDbContext>()
            .UseInMemoryDatabase(_database)
            .ConfigureWarnings(warnings => warnings.Ignore(
                InMemoryEventId.TransactionIgnoredWarning, CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options,
        company);

    public async ValueTask DisposeAsync()
    {
        if (Db is not null)
        {
            await Db.DisposeAsync();
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
