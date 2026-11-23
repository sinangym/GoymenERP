using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// WP7-2 (AI-D31/AI-D32, KIM-D28): the company VKN change Work inbox source lists the caller company's pending request with its
/// preparer as maker, flags a stale request, and is denied without a company-wide approve grant. SENTETIK / NOT_ENV.
/// </summary>
public sealed class OrganizationCompanyTaxIdChangeWorkInboxSourceV1Tests
{
    private const string CurrentVkn = "1234567890";
    private const string ProposedVkn = "4100046586";
    private static readonly Guid CompanyA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);
    private readonly string database = "organization-work-inbox-" + Guid.NewGuid().ToString("N");
    private bool granted = true;
    private readonly List<string> asked = [];

    [Fact]
    public async Task A_pending_request_is_listed_with_its_preparer_as_maker_and_a_current_one_is_not_stale()
    {
        var request = await SeedAsync(moveCompany: false);

        var result = await Source().ListPendingAsync(Caller(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        var item = Assert.Single(result.Items!);
        Assert.Equal(("organization", "organization.company-tax-id-change.v1"), (item.Module, item.Kind));
        Assert.Equal(request, item.ItemId);
        Assert.Equal(ProposedVkn, item.DocumentLabel);
        Assert.Equal("/ayarlar/organizasyon/sirket-vergi-no", item.Link);
        Assert.Equal("SENTETIK-maker", item.PreparedBy);
        Assert.Equal(Start, item.WaitingSinceUtc);
        Assert.Equal([OrganizationCompanyTaxIdChangeWorkInboxSourceV1.ApprovalRequiredRule], item.Rules.Select(rule => rule.RuleCode));
        Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule("organization", rule)));
        Assert.True(NativeApprovalQueueRulesV1.IsValidLink(item.Link));
        Assert.All(asked, permission => Assert.Equal(OrganizationCompanyTaxIdPermissionsV1.Approve, permission));
    }

    [Fact]
    public async Task A_request_prepared_against_an_older_company_version_is_flagged_stale()
    {
        await SeedAsync(moveCompany: true);

        var item = Assert.Single((await Source().ListPendingAsync(Caller(), 5, CancellationToken.None)).Items!);

        Assert.Contains(OrganizationCompanyTaxIdChangeWorkInboxSourceV1.StaleRule, item.Rules.Select(rule => rule.RuleCode));
    }

    [Fact]
    public async Task Without_the_approve_grant_the_source_is_denied_without_items()
    {
        await SeedAsync(moveCompany: false);
        granted = false;

        var result = await Source().ListPendingAsync(Caller(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Items);
    }

    private async Task<Guid> SeedAsync(bool moveCompany)
    {
        // Company rows are created only by the bootstrap seed; the test opens the same narrow scope.
        var bootstrap = new OrganizationBootstrapWriteScope();
        var company = new Company(CompanyA, "SYN-A", "Sentetik Şirket A", CurrentVkn, isPlaceholder: false, invoiceCapable: true);
        using (bootstrap.BeginBootstrapWrite())
        {
            await using var seed = new OrganizationDbContext(
                new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(database).Options, new CompanyContext(), bootstrap);
            seed.Companies.Add(company);
            await seed.SaveChangesAsync();
        }

        await using var context = Context();
        var request = new CompanyTaxIdChangeRequest(Guid.NewGuid(), CompanyA, CurrentVkn, ProposedVkn,
            moveCompany ? company.Version + 1 : company.Version, "SENTETIK gerekçe", "SENTETIK-maker", Start);
        context.CompanyTaxIdChangeRequests.Add(request);
        await context.SaveChangesAsync();
        return request.Id;
    }

    private OrganizationCompanyTaxIdChangeWorkInboxSourceV1 Source() =>
        new(Context(), new WorkInboxCompanyWideFenceV1(Resolved(), new User(), new Authority(this)));

    private OrganizationDbContext Context() => new(
        new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(database).Options, Resolved(),
        new OrganizationBootstrapWriteScope());

    private static CompanyContext Resolved()
    {
        var context = new CompanyContext();
        context.Set(CompanyA);
        return context;
    }

    private static WorkInboxSourceContextV1 Caller() => new(CompanyA, "SENTETIK-checker", "SENTETIK-correlation", null);

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-checker";
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { CompanyA };
        public bool CanAccessCompany(Guid companyId) => companyId == CompanyA;
    }

    private sealed class Authority(OrganizationCompanyTaxIdChangeWorkInboxSourceV1Tests test) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            test.asked.Add(permission);
            return Task.FromResult(new PermissionScope(test.granted, true, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
}
