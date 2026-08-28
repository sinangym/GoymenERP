using System.Net;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
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
        var codes = listJson.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(2, codes.Count);
        Assert.Contains("CAR-000001", codes);
        Assert.Contains("CAR-000002", codes);
        Assert.DoesNotContain("CAR-000003", codes);

        var shared = listJson.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == SharedPartyId);
        var sharedAccounts = shared.GetProperty("companyAccounts").EnumerateArray().ToArray();
        Assert.Single(sharedAccounts);
        Assert.Equal(
            TestCompanyIds.CompanyA,
            sharedAccounts[0].GetProperty("companyId").GetGuid());

        using var hiddenDetailResponse = await client.GetAsync(
            $"/api/v1/counterparties/{CompanyBOnlyPartyId}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenDetailResponse.StatusCode);

        using var unauthorizedCompanyFilter = await client.GetAsync(
            $"/api/v1/counterparties?companyId={TestCompanyIds.CompanyB}");
        Assert.Equal(HttpStatusCode.OK, unauthorizedCompanyFilter.StatusCode);
        using var unauthorizedJson = JsonDocument.Parse(
            await unauthorizedCompanyFilter.Content.ReadAsStringAsync());
        Assert.Empty(unauthorizedJson.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task GroupAdmin_SeesSharedPartyOnceWithAuthorizedCompanyBreakdown()
    {
        await PrepareScenarioAsync();
        using var client = factory.CreateClientFor(TestUsers.GroupAdmin);

        using var response = await client.GetAsync("/api/v1/counterparties");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var parties = json.RootElement.EnumerateArray().ToArray();

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
