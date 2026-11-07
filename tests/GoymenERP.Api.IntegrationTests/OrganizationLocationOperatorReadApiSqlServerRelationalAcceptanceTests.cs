using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.OrganizationRuntime;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class OrganizationLocationOperatorReadApiSqlServerRelationalAcceptanceTests
{
    private const string Actor = "organization-location-operator-reader";
    private static readonly Guid SiteId = Guid.Parse("73000000-0000-4000-8000-000000000001");
    private static readonly Guid LocationOne = Guid.Parse("75000000-0000-4000-8000-000000000001");
    private static readonly Guid LocationTwo = Guid.Parse("75000000-0000-4000-8000-000000000002");
    private static readonly Guid EligibleOne = Guid.Parse("74000000-0000-4000-8000-000000000001");
    private static readonly Guid EligibleTwo = Guid.Parse("74000000-0000-4000-8000-000000000002");
    private static readonly Guid InactiveParty = Guid.Parse("74000000-0000-4000-8000-000000000003");

    [SqlServerFact]
    public async Task Exact_location_reader_gets_display_and_writer_gets_opaque_supplier_candidates_read_only()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        await using var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        using (var health = await factory.CreateClient().GetAsync("/health/live"))
        {
            health.EnsureSuccessStatusCode();
        }

        await SeedOrganizationAsync(factory);
        await SeedCounterpartiesAsync(factory.DatabaseConnectionString);
        await GrantExactLocationAsync(factory);

        using var client = factory.CreateClientFor(Actor, TestCompanyIds.CompanyA);
        using (var assign = await SendAsync(
                   client,
                   HttpMethod.Put,
                   $"/api/v1/organization/locations/{LocationOne:D}/operator",
                   new SetLocationOperator(EligibleOne, 1, "Sentetik harici depo operatörü")))
        {
            Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        }

        var before = await ReadEffectsAsync(factory.DatabaseConnectionString);
        using (var display = await client.GetAsync(
                   $"/api/v1/organization/locations/{LocationOne:D}/operator"))
        {
            Assert.Equal(HttpStatusCode.OK, display.StatusCode);
            AssertPrivateNoStore(display);
            var body = (await display.Content.ReadFromJsonAsync<OrganizationLocationOperatorContextV1>())!;
            Assert.Equal(LocationOne, body.LocationId);
            Assert.Equal((EligibleOne, "CAR-000100", "Birinci Tedarikçi", "Birinci Ticaret", true),
                (body.Operator!.PartyId,
                    body.Operator.Code,
                    body.Operator.LegalName,
                    body.Operator.TradeName,
                    body.Operator.IsCurrentlyEligible));
        }

        string cursor;
        using (var first = await client.GetAsync(
                   $"/api/v1/organization/locations/{LocationOne:D}/operator-candidates?pageSize=1"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            AssertPrivateNoStore(first);
            var page = (await first.Content.ReadFromJsonAsync<OrganizationLocationOperatorCandidatePageV1>())!;
            Assert.True(page.HasMore);
            cursor = Assert.IsType<string>(page.NextCursor);
            Assert.Equal(EligibleOne, Assert.Single(page.Items).PartyId);
        }

        using (var second = await client.GetAsync(
                   $"/api/v1/organization/locations/{LocationOne:D}/operator-candidates?pageSize=1&cursor={Uri.EscapeDataString(cursor)}"))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var page = (await second.Content.ReadFromJsonAsync<OrganizationLocationOperatorCandidatePageV1>())!;
            Assert.Equal(EligibleTwo, Assert.Single(page.Items).PartyId);
            Assert.DoesNotContain(page.Items, item => item.PartyId == InactiveParty);
        }

        using (var mismatched = await client.GetAsync(
                   $"/api/v1/organization/locations/{LocationOne:D}/operator-candidates?q=-&pageSize=1&cursor={Uri.EscapeDataString(cursor)}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);
            using var body = JsonDocument.Parse(await mismatched.Content.ReadAsStringAsync());
            Assert.Equal("invalid_cursor", body.RootElement.GetProperty("code").GetString());
        }

        foreach (var path in new[]
                 {
                     $"/api/v1/organization/locations/{LocationTwo:D}/operator",
                     $"/api/v1/organization/locations/{LocationTwo:D}/operator-candidates",
                     $"/api/v1/organization/locations/{Guid.NewGuid():D}/operator-candidates",
                 })
        {
            using var hidden = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            using var body = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.TryGetProperty("code", out _));
        }

        Assert.Equal(before, await ReadEffectsAsync(factory.DatabaseConnectionString));
    }

    private static async Task SeedOrganizationAsync(SqlServerRelationalApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        db.Sites.Add(new Site(
            SiteId,
            TestCompanyIds.CompanyA,
            "SYN-OP-SITE",
            "Sentetik Harici Depolar",
            SiteType.ExternalWarehouse));
        db.Locations.AddRange(
            new Location(LocationOne, TestCompanyIds.CompanyA, SiteId, "SYN-OP-1", "Birinci Harici Depo", true),
            new Location(LocationTwo, TestCompanyIds.CompanyA, SiteId, "SYN-OP-2", "İkinci Harici Depo", true));
        await db.SaveChangesAsync();
    }

    private static async Task GrantExactLocationAsync(SqlServerRelationalApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userId = Guid.NewGuid();
        db.Users.Add(new UserAccount(userId, "Synthetic location operator reader", null));
        db.ExternalIdentities.Add(new ExternalIdentityLink(
            Guid.NewGuid(),
            userId,
            BootstrapIdentityOptions.Issuer,
            Actor));
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, "SYN-ORG-OPERATOR", "Synthetic exact location operator", true);
        role.AddPermission(ErpPermissions.OrganizationLocationsRead);
        role.AddPermission(OrganizationPermissionManifestV1.LocationsWrite);
        var assignment = new RoleAssignment(
            Guid.NewGuid(),
            userId,
            roleId,
            TestCompanyIds.CompanyA,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            null);
        assignment.AddScope(AccessScopeType.Location, LocationOne);
        db.Roles.Add(role);
        db.RoleAssignments.Add(assignment);
        await db.SaveChangesAsync();
    }

    private static async Task SeedCounterpartiesAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO [counterparty].[Parties]
                ([Id], [Code], [Kind], [LegalName], [TradeName], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@One, N'CAR-000100', N'LegalEntity', N'Birinci Tedarikçi', N'Birinci Ticaret', 1, 1, @Now, @Now),
                (@Two, N'CAR-000200', N'LegalEntity', N'İkinci Tedarikçi', NULL, 1, 1, @Now, @Now),
                (@Inactive, N'CAR-000300', N'LegalEntity', N'Pasif Tedarikçi', NULL, 0, 1, @Now, @Now);

            INSERT INTO [counterparty].[CompanyPartyAccounts]
                ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (NEWID(), @Company, @One, @Supplier, NULL, 1, 1, @Now, @Now),
                (NEWID(), @Company, @Two, @Supplier, NULL, 1, 1, @Now, @Now),
                (NEWID(), @Company, @Inactive, @Supplier, NULL, 1, 1, @Now, @Now);
            """;
        command.Parameters.AddWithValue("@One", EligibleOne);
        command.Parameters.AddWithValue("@Two", EligibleTwo);
        command.Parameters.AddWithValue("@Inactive", InactiveParty);
        command.Parameters.AddWithValue("@Company", TestCompanyIds.CompanyA);
        command.Parameters.AddWithValue("@Supplier", (int)PartyRole.Supplier);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        Assert.Equal(6, await command.ExecuteNonQueryAsync());
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object body)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static async Task<(int Parties, int Accounts, int Locations, int Commands, int Audits)> ReadEffectsAsync(
        string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM [counterparty].[Parties]),
                (SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts]),
                (SELECT COUNT(*) FROM [organization].[Locations]),
                (SELECT COUNT(*) FROM [idempotency].[CommandExecutions]),
                (SELECT COUNT(*) FROM [audit].[AuditEvents]);
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4));
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }
}
