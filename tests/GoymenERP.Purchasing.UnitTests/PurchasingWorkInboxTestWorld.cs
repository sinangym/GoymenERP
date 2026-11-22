using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// W1-A test support for the Purchasing Work inbox sources: one in-memory Purchasing database shared by per-company contexts
/// (each seeded under its own company, as the write guards require), the caller's user and a switchable authority answer.
/// SENTETIK / NOT_ENV.
/// </summary>
internal sealed class PurchasingWorkInboxTestWorld
{
    public static readonly Guid CompanyA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    public static readonly Guid CompanyB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    public const string Approver = "SENTETIK-approver";
    public const string Maker = "SENTETIK-maker";

    private readonly string databaseName = "purchasing-work-inbox-" + Guid.NewGuid().ToString("N");

    public bool Granted { get; set; } = true;

    public bool CompanyWide { get; set; } = true;

    public string? LastPermission { get; private set; }

    public string UserId { get; set; } = Approver;

    public PurchasingDbContext Context(Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseInMemoryDatabase(databaseName).Options, context);
    }

    public async Task SeedAsync(Guid company, Action<PurchasingDbContext> seed)
    {
        await using var context = Context(company);
        seed(context);
        await context.SaveChangesAsync();
    }

    public PurchasingWorkInboxFence Fence(Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return new PurchasingWorkInboxFence(context, new User(this), new Authority(this));
    }

    public WorkInboxSourceContextV1 Caller(Guid company) => new(company, UserId, "SENTETIK-correlation", null);

    public static void AssertContractShape(string module, WorkInboxItemV1 item)
    {
        Assert.Equal(module, item.Module);
        Assert.False(string.IsNullOrWhiteSpace(item.DocumentLabel));
        Assert.True(item.DocumentLabel.Length <= WorkInboxRulesV1.MaxDocumentLabelLength);
        Assert.Equal(TimeSpan.Zero, item.WaitingSinceUtc.Offset);
        Assert.True(NativeApprovalQueueRulesV1.IsValidLink(item.Link), item.Link);
        Assert.False(string.IsNullOrWhiteSpace(item.PreparedBy));
        Assert.InRange(item.Rules.Count, 1, WorkInboxRulesV1.MaxRulesPerItem);
        Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule(module, rule), rule.RuleCode));
        Assert.InRange(item.References.Count, 0, WorkInboxRulesV1.MaxReferencesPerItem);
        Assert.All(item.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference), reference.Role));
    }

    private sealed class User(PurchasingWorkInboxTestWorld world) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => world.UserId;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { CompanyA, CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority(PurchasingWorkInboxTestWorld world) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            world.LastPermission = permission;
            return Task.FromResult(new PermissionScope(world.Granted, world.CompanyWide,
                new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
}
