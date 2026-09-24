using System.Net;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Spec §14.2 "Aynı Party'nin şirket ilişkilerini gör": the read the C-03 company
/// relationships panel consumes, <c>GET /api/v1/counterparties/{partyId}</c> sent
/// WITHOUT <c>X-Company-Id</c> and with the client contract declaration.
///
/// What the panel relies on, and what is pinned here:
/// <list type="bullet">
/// <item>one row per authorized company account, ordered by company id, each with its OWN
/// active state, roles (0 included) and lossless string version, never the Party's;</item>
/// <item>an actor authorized in one company gets exactly that company's account and no
/// trace of the other company's account id or company id;</item>
/// <item>a company in the actor's directory is listed only where the actor holds
/// COMPANY-WIDE <c>counterparty.parties.read</c>: membership without it, or a narrow grant,
/// lists nothing there;</item>
/// <item>the same read WITH a company header is bound to that company (the drill-down);</item>
/// <item>a GET changes nothing: the second read is identical and no account row moves.</item>
/// </list>
/// </summary>
public sealed class CounterpartyCompanyRelationshipsReadAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private const string ContractHeader = "X-ERP-Counterparty-Contract";
    private const string ContractVersion = "optional-trade-roles-v1";

    // Seeded bootstrap users no other case in this class uses. Their extra assignments are
    // created by PrepareMixedScopeActorsAsync in this class's own in-memory identity store.
    private const string MemberWithoutReadInB = TestUsers.CompanyAViewer;
    private const string NarrowReadInB = TestUsers.SiteAuditReader;

    private static readonly Guid PartyId = Guid.Parse("53000000-0000-0000-0000-000000000001");
    private static readonly Guid AccountAId = Guid.Parse("54000000-0000-0000-0000-000000000001");
    private static readonly Guid AccountBId = Guid.Parse("54000000-0000-0000-0000-000000000002");
    private static readonly Guid PartiesReaderRoleId = Guid.Parse("55000000-0000-0000-0000-000000000001");
    private static readonly Guid MemberRoleId = Guid.Parse("55000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Group_read_lists_every_authorized_company_account_with_its_own_state()
    {
        await PrepareScenarioAsync();
        using var client = ContractClient(TestUsers.GroupAdmin);

        using var response = await client.GetAsync($"/api/v1/counterparties/{PartyId}");

        await AssertOkAsync(response);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            ContractVersion,
            Assert.Single(response.Headers.GetValues(ContractHeader)));

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(PartyId, root.GetProperty("id").GetGuid());
        // The GLOBAL party state stays active while one company's account is passive.
        Assert.True(root.GetProperty("isActive").GetBoolean());

        var accounts = root.GetProperty("companyAccounts").EnumerateArray().ToArray();
        Assert.Equal(2, accounts.Length);
        Assert.Equal(
            [TestCompanyIds.CompanyA, TestCompanyIds.CompanyB],
            accounts.Select(account => account.GetProperty("companyId").GetGuid()));

        var a = accounts[0];
        Assert.Equal(AccountAId, a.GetProperty("id").GetGuid());
        Assert.True(a.GetProperty("isActive").GetBoolean());
        Assert.Equal((int)PartyRole.Customer, a.GetProperty("roles").GetInt32());
        Assert.Equal(JsonValueKind.String, a.GetProperty("version").ValueKind);

        var b = accounts[1];
        Assert.Equal(AccountBId, b.GetProperty("id").GetGuid());
        Assert.False(b.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, b.GetProperty("roles").GetInt32());
        Assert.Equal(JsonValueKind.String, b.GetProperty("version").ValueKind);
    }

    [Theory]
    [InlineData(TestUsers.CompanyA, true)]
    [InlineData(TestUsers.CompanyB, false)]
    public async Task Single_company_actor_group_read_never_reveals_the_other_company(
        string userId,
        bool ownsCompanyA)
    {
        await PrepareScenarioAsync();
        using var client = ContractClient(userId);

        using var response = await client.GetAsync($"/api/v1/counterparties/{PartyId}");

        await AssertOkAsync(response);
        var body = await response.Content.ReadAsStringAsync();
        var (ownCompanyId, ownAccountId, otherCompanyId, otherAccountId) = ownsCompanyA
            ? (TestCompanyIds.CompanyA, AccountAId, TestCompanyIds.CompanyB, AccountBId)
            : (TestCompanyIds.CompanyB, AccountBId, TestCompanyIds.CompanyA, AccountAId);

        using var json = JsonDocument.Parse(body);
        var account = Assert.Single(json.RootElement.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(ownCompanyId, account.GetProperty("companyId").GetGuid());
        Assert.Equal(ownAccountId, account.GetProperty("id").GetGuid());
        Assert.DoesNotContain(otherCompanyId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(otherAccountId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Membership is not read authority (FİNANS #267 P2-1). Company B is in this actor's
    /// directory through a role that carries no <c>counterparty.parties.read</c>; read is
    /// company-wide only in A. Only the per-company check can keep B out of the answer.
    /// </summary>
    [Fact]
    public async Task Group_read_lists_no_member_company_without_parties_read()
    {
        await PrepareScenarioAsync();
        await PrepareMixedScopeActorsAsync();
        using var client = ContractClient(MemberWithoutReadInB);
        await AssertDirectoryListsBothCompaniesAsync(client);

        using var response = await client.GetAsync($"/api/v1/counterparties/{PartyId}");

        await AssertOnlyCompanyAAccountAsync(response);
    }

    /// <summary>
    /// Only a COMPANY-WIDE grant counts (ÜRÜN #267 P2-2). The actor reads company-wide in A and
    /// only site-scoped in B: the header-less read lists A alone, and the drill-down into B is
    /// the same non-disclosing 404 a missing record gets.
    /// </summary>
    [Fact]
    public async Task Group_read_lists_no_company_where_parties_read_is_narrow()
    {
        await PrepareScenarioAsync();
        await PrepareMixedScopeActorsAsync();
        using var client = ContractClient(NarrowReadInB);
        await AssertDirectoryListsBothCompaniesAsync(client);

        using var response = await client.GetAsync($"/api/v1/counterparties/{PartyId}");
        await AssertOnlyCompanyAAccountAsync(response);

        using var drillDownClient = ContractClient(NarrowReadInB, TestCompanyIds.CompanyB);
        using var drillDown = await drillDownClient.GetAsync($"/api/v1/counterparties/{PartyId}");
        var drillDownBody = await drillDown.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, drillDown.StatusCode);
        using var problem = JsonDocument.Parse(drillDownBody);
        Assert.Equal(
            CounterpartyProblemCodesV1.NotFound,
            problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(AccountBId.ToString("D"), drillDownBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Drill_down_read_with_a_company_header_is_bound_to_that_company()
    {
        await PrepareScenarioAsync();
        using var client = ContractClient(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);

        using var response = await client.GetAsync($"/api/v1/counterparties/{PartyId}");

        await AssertOkAsync(response);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var account = Assert.Single(json.RootElement.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(TestCompanyIds.CompanyB, account.GetProperty("companyId").GetGuid());
        Assert.False(account.GetProperty("isActive").GetBoolean());
        Assert.DoesNotContain(AccountAId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Group_read_has_no_side_effect()
    {
        await PrepareScenarioAsync();
        var before = await SnapshotAccountsAsync();
        using var client = ContractClient(TestUsers.GroupAdmin);

        using var first = await client.GetAsync($"/api/v1/counterparties/{PartyId}");
        using var second = await client.GetAsync($"/api/v1/counterparties/{PartyId}");

        await AssertOkAsync(first);
        await AssertOkAsync(second);
        Assert.Equal(
            await first.Content.ReadAsStringAsync(),
            await second.Content.ReadAsStringAsync());
        Assert.Equal(before, await SnapshotAccountsAsync());
    }

    private static async Task AssertOkAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            Assert.Fail($"Expected 200, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    private static async Task AssertDirectoryListsBothCompaniesAsync(HttpClient client)
    {
        // The precondition that makes the case mean something: B IS in this actor's directory.
        using var response = await client.GetAsync("/api/v1/organization/companies");
        await AssertOkAsync(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var companyIds = json.RootElement
            .EnumerateArray()
            .Select(company => company.GetProperty("id").GetGuid())
            .ToArray();
        Assert.Contains(TestCompanyIds.CompanyA, companyIds);
        Assert.Contains(TestCompanyIds.CompanyB, companyIds);
    }

    private static async Task AssertOnlyCompanyAAccountAsync(HttpResponseMessage response)
    {
        await AssertOkAsync(response);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var account = Assert.Single(json.RootElement.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(TestCompanyIds.CompanyA, account.GetProperty("companyId").GetGuid());
        Assert.Equal(AccountAId, account.GetProperty("id").GetGuid());
        Assert.DoesNotContain(TestCompanyIds.CompanyB.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AccountBId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient ContractClient(string userId, Guid? companyId = null)
    {
        // The factory already declares the contract on every client (ConfigureClient).
        // Pinned rather than re-added: a second value is itself an unsupported declaration.
        var client = factory.CreateClientFor(userId, companyId);
        Assert.Equal(
            ContractVersion,
            Assert.Single(client.DefaultRequestHeaders.GetValues(ContractHeader)));
        return client;
    }

    private async Task<string> SnapshotAccountsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
        var rows = await dbContext.CompanyPartyAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(account => account.PartyId == PartyId)
            .OrderBy(account => account.Id)
            .Select(account => new
            {
                account.Id,
                account.IsActive,
                account.Roles,
                account.Version,
                account.UpdatedAtUtc,
            })
            .ToListAsync();
        var partyCount = await dbContext.Parties.IgnoreQueryFilters().CountAsync();
        var accountCount = await dbContext.CompanyPartyAccounts.IgnoreQueryFilters().CountAsync();
        return JsonSerializer.Serialize(new { rows, partyCount, accountCount });
    }

    private async Task PrepareScenarioAsync()
    {
        _ = factory.Services;
        await using (var grantScope = factory.Services.CreateAsyncScope())
        {
            var identityDbContext = grantScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var operatorRole = await identityDbContext.Roles
                .Include(role => role.Permissions)
                .SingleAsync(role => role.Code == "ORGANIZATION_OPERATOR");
            operatorRole.AddPermission(ErpPermissions.CounterpartyPartiesRead);
            await identityDbContext.SaveChangesAsync();
        }

        await using (var checkScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = checkScope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            if (await dbContext.Parties.IgnoreQueryFilters().AnyAsync(party => party.Id == PartyId))
            {
                return;
            }
        }

        await using (var scopeA = factory.Services.CreateAsyncScope())
        {
            scopeA.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var dbContext = scopeA.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            dbContext.Parties.Add(new Party(
                PartyId,
                PartyCode.Parse("CAR-000501"),
                PartyKind.LegalEntity,
                "İlişkiler Ortak Cari A.Ş."));
            dbContext.CompanyPartyAccounts.Add(new CompanyPartyAccount(
                AccountAId,
                TestCompanyIds.CompanyA,
                PartyId,
                PartyRole.Customer));
            await dbContext.SaveChangesAsync();
        }

        await using (var scopeB = factory.Services.CreateAsyncScope())
        {
            scopeB.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyB);
            var dbContext = scopeB.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            var accountB = new CompanyPartyAccount(
                AccountBId,
                TestCompanyIds.CompanyB,
                PartyId,
                PartyRole.None);
            accountB.Deactivate();
            dbContext.CompanyPartyAccounts.Add(accountB);
            await dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Test-local roles and assignments for the two mixed-scope actors. Nothing shared moves:
    /// no seed file, no system role, no actor another case here uses. Runs once per fixture.
    /// </summary>
    private async Task PrepareMixedScopeActorsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        if (await identity.Roles.AnyAsync(role => role.Id == PartiesReaderRoleId))
        {
            return;
        }

        var organization = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var companyBSiteId = await organization.Sites
            .IgnoreQueryFilters()
            .Where(site => site.CompanyId == TestCompanyIds.CompanyB)
            .OrderBy(site => site.Id)
            .Select(site => site.Id)
            .FirstAsync();

        var partiesReader = new RoleDefinition(
            PartiesReaderRoleId,
            "TEST_RELATIONSHIPS_PARTIES_READER",
            "İlişkiler Cari Okuma Test Rolü",
            isSystem: false);
        partiesReader.AddPermission(ErpPermissions.CounterpartyPartiesRead);
        // Company membership through a role that holds no counterparty permission at all.
        var member = new RoleDefinition(
            MemberRoleId,
            "TEST_RELATIONSHIPS_MEMBER",
            "İlişkiler Üyelik Test Rolü",
            isSystem: false);
        member.AddPermission(ErpPermissions.OrganizationContextRead);
        identity.Roles.AddRange(partiesReader, member);

        var memberWithoutRead = await UserIdOfAsync(identity, MemberWithoutReadInB);
        identity.RoleAssignments.AddRange(
            Assignment(memberWithoutRead, PartiesReaderRoleId, TestCompanyIds.CompanyA, AccessScopeType.Company, TestCompanyIds.CompanyA),
            Assignment(memberWithoutRead, MemberRoleId, TestCompanyIds.CompanyB, AccessScopeType.Company, TestCompanyIds.CompanyB));

        var narrowRead = await UserIdOfAsync(identity, NarrowReadInB);
        identity.RoleAssignments.AddRange(
            Assignment(narrowRead, PartiesReaderRoleId, TestCompanyIds.CompanyA, AccessScopeType.Company, TestCompanyIds.CompanyA),
            Assignment(narrowRead, PartiesReaderRoleId, TestCompanyIds.CompanyB, AccessScopeType.Site, companyBSiteId));

        await identity.SaveChangesAsync();
    }

    private static async Task<Guid> UserIdOfAsync(IdentityDbContext identity, string subject)
    {
        return await identity.ExternalIdentities
            .Where(external => external.Subject == subject)
            .Select(external => external.UserId)
            .SingleAsync();
    }

    private static RoleAssignment Assignment(
        Guid userId,
        Guid roleId,
        Guid companyId,
        AccessScopeType scopeType,
        Guid scopeId)
    {
        var assignment = new RoleAssignment(
            Guid.NewGuid(),
            userId,
            roleId,
            companyId,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            null);
        assignment.AddScope(scopeType, scopeId);
        return assignment;
    }
}
