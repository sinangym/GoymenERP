using System.Net;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyReadAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid SharedPartyId = Guid.Parse(
        "51000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAOnlyPartyId = Guid.Parse(
        "51000000-0000-0000-0000-000000000002");
    private static readonly Guid CompanyBOnlyPartyId = Guid.Parse(
        "51000000-0000-0000-0000-000000000003");

    [Fact]
    public async Task CompanyA_User_CannotObserveCompanyBOnlyParty()
    {
        await PrepareScenarioAsync();
        using var client = factory.CreateClientFor(TestUsers.CompanyA);

        using var listResponse = await client.GetAsync("/api/v1/counterparties");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var items = listJson.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var codes = items
            .Select(item => item.GetProperty("code").GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(2, codes.Count);
        Assert.Contains("CAR-000001", codes);
        Assert.Contains("CAR-000002", codes);
        Assert.DoesNotContain("CAR-000003", codes);

        var shared = items
            .Single(item => item.GetProperty("id").GetGuid() == SharedPartyId);
        var sharedAccounts = shared.GetProperty("companyAccounts").EnumerateArray().ToArray();
        Assert.Single(sharedAccounts);
        Assert.Equal(
            TestCompanyIds.CompanyA,
            sharedAccounts[0].GetProperty("companyId").GetGuid());
        Assert.False(sharedAccounts[0].TryGetProperty("externalIntegrationCode", out _));

        using var hiddenDetailResponse = await client.GetAsync(
            $"/api/v1/counterparties/{CompanyBOnlyPartyId}");
        var hiddenBody = await AssertProblemCodeAsync(
            hiddenDetailResponse,
            HttpStatusCode.NotFound,
            CounterpartyProblemCodesV1.NotFound);
        Assert.DoesNotContain(
            CompanyBOnlyPartyId.ToString("D"),
            hiddenBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            TestCompanyIds.CompanyB.ToString("D"),
            hiddenBody,
            StringComparison.OrdinalIgnoreCase);

        using var unauthorizedCompanyFilter = await client.GetAsync(
            $"/api/v1/counterparties?companyId={TestCompanyIds.CompanyB}");
        await AssertProblemCodeAsync(
            unauthorizedCompanyFilter,
            HttpStatusCode.BadRequest,
            CounterpartyProblemCodesV1.ValidationFailed);
    }

    [Fact]
    public async Task GroupAdmin_SeesSharedPartyOnceWithAuthorizedCompanyBreakdown()
    {
        await PrepareScenarioAsync();
        using var client = factory.CreateClientFor(TestUsers.GroupAdmin);

        using var response = await client.GetAsync("/api/v1/counterparties");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var parties = json.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(3, parties.Length);
        Assert.Equal(1, parties.Count(item => item.GetProperty("id").GetGuid() == SharedPartyId));

        var shared = parties.Single(item => item.GetProperty("id").GetGuid() == SharedPartyId);
        var companyIds = shared.GetProperty("companyAccounts")
            .EnumerateArray()
            .Select(item => item.GetProperty("companyId").GetGuid())
            .ToHashSet();
        Assert.Equal(2, companyIds.Count);
        Assert.Contains(TestCompanyIds.CompanyA, companyIds);
        Assert.Contains(TestCompanyIds.CompanyB, companyIds);
        Assert.False(json.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("totalCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("countScope").ValueKind);

        using var customerResponse = await client.GetAsync(
            "/api/v1/counterparties?role=Customer");
        Assert.Equal(HttpStatusCode.OK, customerResponse.StatusCode);
        using var customerJson = JsonDocument.Parse(
            await customerResponse.Content.ReadAsStringAsync());
        var customerShared = customerJson.RootElement.GetProperty("items")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == SharedPartyId);
        var customerAccount = Assert.Single(
            customerShared.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(
            TestCompanyIds.CompanyA,
            customerAccount.GetProperty("companyId").GetGuid());
    }

    [Fact]
    public async Task PageCursor_IsOpaqueBoundAndTamperProof_WithExactAuthorizedCount()
    {
        await PrepareScenarioAsync();
        using var client = factory.CreateClientFor(TestUsers.GroupAdmin);

        using var firstResponse = await client.GetAsync(
            "/api/v1/counterparties?pageSize=1&includeCount=true&partyState=all");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var first = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        Assert.True(first.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Equal(3, first.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Equal(
            "filtered-authorized-visible-parties",
            first.RootElement.GetProperty("countScope").GetString());
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));

        using var secondResponse = await client.GetAsync(
            $"/api/v1/counterparties?pageSize=1&includeCount=true&partyState=all&cursor={Uri.EscapeDataString(cursor!)}");
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var tampered = cursor![..^1] + (cursor[^1] == 'A' ? 'B' : 'A');
        using var tamperedResponse = await client.GetAsync(
            $"/api/v1/counterparties?pageSize=1&includeCount=true&partyState=all&cursor={Uri.EscapeDataString(tampered)}");
        await AssertProblemCodeAsync(
            tamperedResponse,
            HttpStatusCode.BadRequest,
            CounterpartyProblemCodesV1.InvalidCursor);

        using var changedFilterResponse = await client.GetAsync(
            $"/api/v1/counterparties?pageSize=2&includeCount=true&partyState=all&cursor={Uri.EscapeDataString(cursor)}");
        await AssertProblemCodeAsync(
            changedFilterResponse,
            HttpStatusCode.BadRequest,
            CounterpartyProblemCodesV1.InvalidCursor);

        using var otherActor = factory.CreateClientFor(TestUsers.CompanyA);
        using var otherActorResponse = await otherActor.GetAsync(
            $"/api/v1/counterparties?pageSize=1&includeCount=true&partyState=all&cursor={Uri.EscapeDataString(cursor)}");
        await AssertProblemCodeAsync(
            otherActorResponse,
            HttpStatusCode.BadRequest,
            CounterpartyProblemCodesV1.InvalidCursor);

        await ChangeAuthorizationRevisionWithoutChangingReadScopeAsync();
        using var changedAuthorization = await client.GetAsync(
            $"/api/v1/counterparties?pageSize=1&includeCount=true&partyState=all&cursor={Uri.EscapeDataString(cursor)}");
        await AssertProblemCodeAsync(
            changedAuthorization,
            HttpStatusCode.BadRequest,
            CounterpartyProblemCodesV1.InvalidCursor);
    }

    [Theory]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=-1")]
    [InlineData("?pageSize=not-a-number")]
    [InlineData("?pageSize=201")]
    [InlineData("?kind=unknown")]
    [InlineData("?role=unknown")]
    [InlineData("?roleMatch=unknown")]
    [InlineData("?partyState=unknown")]
    [InlineData("?accountState=unknown")]
    [InlineData("?sort=unknown")]
    [InlineData("?direction=unknown")]
    [InlineData("?role=Customer,Supplier")]
    [InlineData("?includeCount=1")]
    [InlineData("?q=ok&q=duplicate")]
    [InlineData("?sort=code&sort=code")]
    [InlineData("?unknown=value")]
    public async Task InvalidRawQuery_FailsClosedWithStableValidationCode(string query)
    {
        await PrepareScenarioAsync();
        using var client = factory.CreateClientFor(TestUsers.GroupAdmin);

        using var response = await client.GetAsync($"/api/v1/counterparties{query}");

        await AssertProblemCodeAsync(
            response,
            HttpStatusCode.BadRequest,
            CounterpartyProblemCodesV1.ValidationFailed);
    }

    [Fact]
    public async Task SearchLongerThanTwoHundredRunes_FailsClosed()
    {
        await PrepareScenarioAsync();
        using var client = factory.CreateClientFor(TestUsers.GroupAdmin);
        var query = new string('a', 201);

        using var response = await client.GetAsync(
            $"/api/v1/counterparties?q={query}");

        await AssertProblemCodeAsync(
            response,
            HttpStatusCode.BadRequest,
            CounterpartyProblemCodesV1.ValidationFailed);
    }

    [Theory]
    [InlineData("?cursor=")]
    [InlineData("?cursor=first&cursor=second")]
    [InlineData("?cursor=%20")]
    public async Task MalformedCursor_FailsClosedWithStableCursorCode(string query)
    {
        await PrepareScenarioAsync();
        using var client = factory.CreateClientFor(TestUsers.GroupAdmin);

        using var response = await client.GetAsync($"/api/v1/counterparties{query}");

        await AssertProblemCodeAsync(
            response,
            HttpStatusCode.BadRequest,
            CounterpartyProblemCodesV1.InvalidCursor);
    }

    [Fact]
    public async Task OptionalRead_MalformedCompanyContext_UsesValidationFailedCode()
    {
        await PrepareScenarioAsync();
        using var client = factory.CreateClientFor(TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", "not-a-company-guid");

        using var response = await client.GetAsync("/api/v1/counterparties");

        await AssertProblemCodeAsync(
            response,
            HttpStatusCode.BadRequest,
            CounterpartyProblemCodesV1.ValidationFailed);
    }

    private static async Task<string> AssertProblemCodeAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            (int)expectedStatus,
            document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("detail").GetString()));
        return body;
    }

    private async Task PrepareScenarioAsync()
    {
        _ = factory.Services;
        await GrantReadPermissionAsync();

        await using (var checkScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = checkScope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            if (await dbContext.Parties.AnyAsync(party => party.Id == SharedPartyId))
            {
                return;
            }
        }

        await SeedCompanyAAsync();
        await SeedCompanyBAsync();
    }

    private async Task GrantReadPermissionAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identityDbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var operatorRole = await identityDbContext.Roles
            .Include(role => role.Permissions)
            .SingleAsync(role => role.Code == "ORGANIZATION_OPERATOR");
        operatorRole.AddPermission(ErpPermissions.CounterpartyPartiesRead);
        await identityDbContext.SaveChangesAsync();
    }

    private async Task ChangeAuthorizationRevisionWithoutChangingReadScopeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identityDbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var administratorRole = await identityDbContext.Roles
            .Include(role => role.Permissions)
            .SingleAsync(role => role.Code == "SYSTEM_ADMINISTRATOR");
        identityDbContext.Entry(administratorRole)
            .Property(role => role.Version)
            .CurrentValue = administratorRole.Version + 1;
        await identityDbContext.SaveChangesAsync();
    }

    private async Task SeedCompanyAAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();

        dbContext.Parties.AddRange(
            new Party(
                SharedPartyId,
                PartyCode.Parse("CAR-000001"),
                PartyKind.LegalEntity,
                "Ortak Cari A.Ş."),
            new Party(
                CompanyAOnlyPartyId,
                PartyCode.Parse("CAR-000002"),
                PartyKind.LegalEntity,
                "Yalnız A Cari A.Ş."));

        dbContext.CompanyPartyAccounts.AddRange(
            new CompanyPartyAccount(
                Guid.Parse("52000000-0000-0000-0000-000000000001"),
                TestCompanyIds.CompanyA,
                SharedPartyId,
                PartyRole.Customer),
            new CompanyPartyAccount(
                Guid.Parse("52000000-0000-0000-0000-000000000003"),
                TestCompanyIds.CompanyA,
                CompanyAOnlyPartyId,
                PartyRole.Customer));

        await dbContext.SaveChangesAsync();
    }

    private async Task SeedCompanyBAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyB);
        var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();

        dbContext.Parties.Add(
            new Party(
                CompanyBOnlyPartyId,
                PartyCode.Parse("CAR-000003"),
                PartyKind.LegalEntity,
                "Yalnız B Cari A.Ş."));

        dbContext.CompanyPartyAccounts.AddRange(
            new CompanyPartyAccount(
                Guid.Parse("52000000-0000-0000-0000-000000000002"),
                TestCompanyIds.CompanyB,
                SharedPartyId,
                PartyRole.Supplier),
            new CompanyPartyAccount(
                Guid.Parse("52000000-0000-0000-0000-000000000004"),
                TestCompanyIds.CompanyB,
                CompanyBOnlyPartyId,
                PartyRole.Supplier));

        await dbContext.SaveChangesAsync();
    }
}
