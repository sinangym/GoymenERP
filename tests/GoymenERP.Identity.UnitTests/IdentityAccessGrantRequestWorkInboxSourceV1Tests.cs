using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// WP7 (AI-D31/AI-D32, KIM-D1, D4): the access grant request Work inbox source lists every Pending request of the company,
/// oldest first, under a company-wide <c>identity.access.approve</c>; on a request that needs a second approval the requester
/// is the maker and the target user may not decide either. SENTETIK / NOT_ENV.
/// </summary>
public sealed class IdentityAccessGrantRequestWorkInboxSourceV1Tests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private readonly string databaseName = "identity-work-inbox-" + Guid.NewGuid().ToString("N");
    private readonly Guid requester = Guid.NewGuid();
    private readonly Guid target = Guid.NewGuid();
    private readonly Guid approver = Guid.NewGuid();
    private readonly Guid roleId = Guid.NewGuid();
    private bool granted = true;
    private bool companyWide = true;
    private readonly List<string> asked = [];

    [Fact]
    public async Task Only_pending_requests_of_the_caller_company_are_listed_oldest_first_with_role_and_target()
    {
        var older = Request(CompanyA, critical: true, Start, validUntil: Start.AddDays(30));
        var newer = Request(CompanyA, critical: true, Start.AddHours(1));
        var decided = Request(CompanyA, critical: true, Start.AddDays(-1));
        decided.Approve(approver, Start.AddDays(-1).AddMinutes(5), Guid.NewGuid());
        await SeedAsync(newer, older, decided, Request(CompanyB, critical: true, Start.AddDays(-2)));

        var result = await Source(approver).ListPendingAsync(Caller(approver), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.False(result.HasMore);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(("identity", "identity.access-grant-request.v1"), (first.Module, first.Kind));
        Assert.Equal("SENTETIK satın alma müdürü", first.DocumentLabel);
        Assert.Equal(Start, first.WaitingSinceUtc);
        Assert.Equal("/ayarlar/kullanicilar-ve-yetkiler?kullanici=" + target.ToString("D"), first.Link);
        Assert.True(NativeApprovalQueueRulesV1.IsValidLink(first.Link));
        Assert.Equal(
            [
                IdentityAccessGrantRequestWorkInboxSourceV1.ApprovalRequiredRule,
                IdentityAccessGrantRequestWorkInboxSourceV1.SecondApprovalRule,
                IdentityAccessGrantRequestWorkInboxSourceV1.TimeBoxedRule,
            ],
            first.Rules.Select(rule => rule.RuleCode));
        Assert.All(first.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule("identity", rule)));
        Assert.Equal(["target-user", "role"], first.References.Select(reference => reference.Role));
        Assert.All(first.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference)));
        Assert.Equal("SENTETIK Hedef Kişi", first.References[0].Label);
        Assert.All(asked, permission => Assert.Equal(IdentityPermissionManifestV1.AccessApprove, permission));
    }

    [Fact]
    public async Task A_critical_request_excludes_its_requester_and_target_and_a_plain_one_excludes_no_one()
    {
        var critical = Request(CompanyA, critical: true, Start);
        var plain = Request(CompanyA, critical: false, Start.AddMinutes(1));
        await SeedAsync(critical, plain);

        var items = (await Source(approver).ListPendingAsync(Caller(approver), 10, CancellationToken.None)).Items!;

        Assert.Equal(requester.ToString("D"), items[0].PreparedBy);
        Assert.True(items[0].Excludes(requester.ToString("D")));
        Assert.True(items[0].Excludes(target.ToString("D")));
        Assert.False(items[0].Excludes(approver.ToString("D")));
        Assert.Null(items[1].PreparedBy);
        Assert.False(items[1].Excludes(requester.ToString("D")));
        Assert.Equal([IdentityAccessGrantRequestWorkInboxSourceV1.ApprovalRequiredRule], items[1].Rules.Select(rule => rule.RuleCode));
    }

    [Fact]
    public async Task A_request_whose_role_is_missing_makes_the_read_unavailable()
    {
        await SeedAsync(Request(CompanyA, critical: true, Start, role: Guid.NewGuid()));

        var result = await Source(approver).ListPendingAsync(Caller(approver), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Items);
    }

    [Fact]
    public async Task More_pending_requests_than_requested_set_has_more()
    {
        var rows = Enumerable.Range(0, 3).Select(index => Request(CompanyA, critical: true, Start.AddMinutes(index))).ToArray();
        await SeedAsync(rows);

        var result = await Source(approver).ListPendingAsync(Caller(approver), 2, CancellationToken.None);

        Assert.True(result.HasMore);
        Assert.Equal([rows[0].Id, rows[1].Id], result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task Without_a_company_wide_approve_grant_or_for_another_user_the_source_is_denied()
    {
        await SeedAsync(Request(CompanyA, critical: true, Start));

        granted = false;
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied,
            (await Source(approver).ListPendingAsync(Caller(approver), 5, CancellationToken.None)).Outcome);
        granted = true;
        companyWide = false;
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied,
            (await Source(approver).ListPendingAsync(Caller(approver), 5, CancellationToken.None)).Outcome);
        companyWide = true;
        var mismatch = await Source(approver).ListPendingAsync(Caller(Guid.NewGuid()), 5, CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, mismatch.Outcome);
        Assert.Null(mismatch.Items);
    }

    private IdentityAccessGrantRequestWorkInboxSourceV1 Source(Guid user)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return new(Context(), company, new User(user), new Authority(this));
    }

    private IdentityDbContext Context() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseInMemoryDatabase(databaseName).Options);

    private static WorkInboxSourceContextV1 Caller(Guid user) => new(CompanyA, user.ToString("D"), "SENTETIK-correlation", null);

    private AccessGrantRequest Request(Guid company, bool critical, DateTimeOffset at, DateTimeOffset? validUntil = null,
        Guid? role = null) =>
        new(Guid.NewGuid(), requester, target, role ?? roleId, company, critical, at, validUntil is null ? null : at, validUntil,
            "SENTETIK gerekçe");

    private async Task SeedAsync(params AccessGrantRequest[] requests)
    {
        await using var context = Context();
        context.Roles.Add(new RoleDefinition(roleId, "SENTETIK-SATINALMA-MUDURU", "SENTETIK satın alma müdürü", isSystem: false));
        context.Users.AddRange(new UserAccount(requester, "SENTETIK Talep Eden", null), new UserAccount(target, "SENTETIK Hedef Kişi", null));
        context.AccessGrantRequests.AddRange(requests);
        await context.SaveChangesAsync();
    }

    private sealed class User(Guid user) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => user.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { CompanyA, CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority(IdentityAccessGrantRequestWorkInboxSourceV1Tests test) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            test.asked.Add(permission);
            return Task.FromResult(new PermissionScope(test.granted, test.companyWide,
                new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
}
